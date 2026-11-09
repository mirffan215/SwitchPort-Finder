// Runs before first paint (external file so the strict CSP needs no inline scripts).
(function () {
  var t = 'dark';
  try { var s = localStorage.getItem('spf.theme'); if (s === 'light' || s === 'dark' || s === 'auto') t = s; } catch (e) {}
  document.documentElement.setAttribute('data-theme', t);
})();
