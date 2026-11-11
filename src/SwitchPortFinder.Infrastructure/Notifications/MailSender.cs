using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using SwitchPortFinder.Application.Abstractions;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Domain.Entities;

namespace SwitchPortFinder.Infrastructure.Notifications;

public record MailMessageData(string Subject, string TextBody, string HtmlBody);
public record MailResult(bool Success, string Message);

/// <summary>
/// Sends e-mail through the configured SMTP server with MailKit: implicit TLS (port 465) or STARTTLS (587) at TLS 1.2/1.3 only, certificate validation ON unless
/// the administrator explicitly trusts an internal relay, SASL authentication (SCRAM/PLAIN/LOGIN as offered), 20 s timeouts. The server address is checked with
/// the same target policy as SNMP so the portal cannot be pointed at loopback, link-local (cloud metadata) or multicast addresses. The password is never logged.
/// </summary>
public class MailSender(ICredentialProtector protector, IOptions<SecurityOptions> security, ILogger<MailSender> log)
{
    /// <summary>MimeKit accepts a bare "nope" as a mailbox; a deliverable address needs a local part, an @ and a domain.</summary>
    public static bool TryAddress(string? text, out MailboxAddress mailbox)
    {
        mailbox = null!;
        if (!MailboxAddress.TryParse(text ?? "", out var m)) return false;
        var at = m.Address.IndexOf('@'); if (at <= 0 || at == m.Address.Length - 1 || m.Address.Length > 254) return false;
        mailbox = m; return true;
    }

    public static string? ConfigProblem(NotificationConfig c)
    {
        if (string.IsNullOrWhiteSpace(c.Host)) return "Enter the mail server host name.";
        if (c.Port is < 1 or > 65535) return "The port must be between 1 and 65535.";
        if (!TryAddress(c.FromAddress, out _)) return "Enter a valid From address.";
        return null;
    }

    public static List<MailboxAddress> ParseRecipients(string? list)
    {
        var res = new List<MailboxAddress>();
        foreach (var part in (list ?? "").Split(new[] { ';', ',', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (TryAddress(part, out var m) && !res.Any(x => x.Address.Equals(m.Address, StringComparison.OrdinalIgnoreCase))) res.Add(m);
        return res;
    }

    public async Task<MailResult> SendAsync(NotificationConfig c, IEnumerable<MailboxAddress> to, MailMessageData msg, CancellationToken ct)
    {
        if (ConfigProblem(c) is { } bad) return new MailResult(false, bad);
        var rcpt = to.ToList(); if (rcpt.Count == 0) return new MailResult(false, "There is no recipient.");
        try
        {
            // the mail server address goes through the same safety policy as an SNMP target
            IPAddress[] ips = IPAddress.TryParse(c.Host, out var lit) ? [lit] : await Dns.GetHostAddressesAsync(c.Host, ct);
            foreach (var ip in ips) if (TargetPolicy.Check(ip.ToString(), security.Value.AllowLoopbackTargets) is { } why) return new MailResult(false, $"The mail server address is not allowed: {why}");

            var m = new MimeMessage();
            m.From.Add(new MailboxAddress(c.FromName, c.FromAddress)); m.To.AddRange(rcpt); m.Subject = msg.Subject;
            m.Body = new BodyBuilder { TextBody = msg.TextBody, HtmlBody = msg.HtmlBody }.ToMessageBody();
            using var client = new SmtpClient { Timeout = 20_000, SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13, CheckCertificateRevocation = false };
            if (c.TrustServerCertificate) client.ServerCertificateValidationCallback = (_, _, _, _) => true;
            var opt = c.Security switch { SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect, SmtpSecurity.StartTls => SecureSocketOptions.StartTls, _ => SecureSocketOptions.None };
            await client.ConnectAsync(c.Host, c.Port, opt, ct);
            if (!string.IsNullOrEmpty(c.Username) && !string.IsNullOrEmpty(c.EncryptedPassword))
                await client.AuthenticateAsync(c.Username, protector.Unprotect(c.EncryptedPassword), ct);
            await client.SendAsync(m, ct); await client.DisconnectAsync(true, ct);
            return new MailResult(true, "Sent.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            var friendly = Friendly(ex);
            log.LogWarning("MailFailed: {Host}:{Port} - {Reason}", c.Host, c.Port, friendly);   // never the exception text: it can echo the user name
            return new MailResult(false, friendly);
        }
    }

    /// <summary>Short, safe explanations an administrator can act on.</summary>
    public static string Friendly(Exception ex) => ex switch
    {
        System.Security.Authentication.AuthenticationException or SslHandshakeException => "TLS/certificate problem: the server's certificate is not trusted or its name does not match. Use the correct host name, install the CA on this server, or (internal relay only) tick 'Trust the server certificate'.",
        SmtpCommandException sc when sc.StatusCode == SmtpStatusCode.MailboxUnavailable || sc.StatusCode == SmtpStatusCode.MailboxNameNotAllowed => "The server refused the recipient or sender address.",
        SmtpCommandException sc when (int)sc.StatusCode >= 530 && (int)sc.StatusCode <= 535 => "The server requires authentication or refused it. Check the user name and password.",
        SmtpCommandException => "The server rejected the message.",
        SmtpProtocolException => "The server did not speak SMTP on that port/security mode. Typical settings: port 587 + STARTTLS, or port 465 + SSL/TLS.",
        MailKit.Security.AuthenticationException => "Authentication failed. Check the user name and password (and that the account may use SMTP).",
        SocketException { SocketErrorCode: SocketError.ConnectionRefused } => "Connection refused: nothing is listening on that host and port.",
        SocketException { SocketErrorCode: SocketError.HostNotFound or SocketError.NoData } => "The mail server name could not be resolved.",
        SocketException or IOException => "Could not connect to the mail server (network or firewall).",
        TimeoutException or OperationCanceledException => "The mail server did not answer in time.",
        _ => "Sending failed. See the server log for the category of the error.",
    };
}
