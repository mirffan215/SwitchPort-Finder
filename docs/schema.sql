IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109100140_InitialCreate'
)
BEGIN
    CREATE TABLE [CredentialProfiles] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(100) NOT NULL,
        [Description] nvarchar(500) NULL,
        [Version] int NOT NULL,
        [AuthProtocol] int NOT NULL,
        [PrivProtocol] int NOT NULL,
        [ContextName] nvarchar(100) NULL,
        [EncryptedSecrets] nvarchar(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_CredentialProfiles] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109100140_InitialCreate'
)
BEGIN
    CREATE TABLE [ImportBatches] (
        [Id] int NOT NULL IDENTITY,
        [FileName] nvarchar(260) NOT NULL,
        [Mode] int NOT NULL,
        [ImportedBy] nvarchar(100) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [TotalRows] int NOT NULL,
        [CreatedCount] int NOT NULL,
        [UpdatedCount] int NOT NULL,
        [SkippedCount] int NOT NULL,
        [ErrorCount] int NOT NULL,
        CONSTRAINT [PK_ImportBatches] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109100140_InitialCreate'
)
BEGIN
    CREATE TABLE [Switches] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(100) NOT NULL,
        [ManagementIp] nvarchar(45) NOT NULL,
        [Manufacturer] int NOT NULL,
        [ManufacturerOther] nvarchar(100) NULL,
        [DetectedManufacturer] int NULL,
        [Model] nvarchar(100) NULL,
        [Location] nvarchar(200) NULL,
        [Notes] nvarchar(1000) NULL,
        [UseAsArpSource] bit NOT NULL,
        [SysObjectId] nvarchar(200) NULL,
        [SysName] nvarchar(255) NULL,
        [SysDescr] nvarchar(1000) NULL,
        [Status] int NOT NULL,
        [LastScanAt] datetime2 NULL,
        [CredentialProfileId] int NULL,
        [ImportBatchId] int NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Switches] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Switches_CredentialProfiles_CredentialProfileId] FOREIGN KEY ([CredentialProfileId]) REFERENCES [CredentialProfiles] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Switches_ImportBatches_ImportBatchId] FOREIGN KEY ([ImportBatchId]) REFERENCES [ImportBatches] ([Id]) ON DELETE SET NULL
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109100140_InitialCreate'
)
BEGIN
    CREATE TABLE [SwitchCredentials] (
        [Id] int NOT NULL IDENTITY,
        [SwitchId] int NOT NULL,
        [Version] int NOT NULL,
        [AuthProtocol] int NOT NULL,
        [PrivProtocol] int NOT NULL,
        [ContextName] nvarchar(100) NULL,
        [EncryptedSecrets] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_SwitchCredentials] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SwitchCredentials_Switches_SwitchId] FOREIGN KEY ([SwitchId]) REFERENCES [Switches] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109100140_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_CredentialProfiles_Name] ON [CredentialProfiles] ([Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109100140_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SwitchCredentials_SwitchId] ON [SwitchCredentials] ([SwitchId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109100140_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Switches_CredentialProfileId] ON [Switches] ([CredentialProfileId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109100140_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Switches_ImportBatchId] ON [Switches] ([ImportBatchId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109100140_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Switches_ManagementIp] ON [Switches] ([ManagementIp]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109100140_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Switches_Name] ON [Switches] ([Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109100140_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261109100140_InitialCreate', N'8.0.11');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109101636_Phase2Discovery'
)
BEGIN
    ALTER TABLE [Switches] ADD [ArpAutoDetected] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109101636_Phase2Discovery'
)
BEGIN
    ALTER TABLE [Switches] ADD [SerialNumber] nvarchar(100) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109101636_Phase2Discovery'
)
BEGIN
    CREATE TABLE [ArpEntries] (
        [Id] bigint NOT NULL IDENTITY,
        [SourceSwitchId] int NOT NULL,
        [Ip] nvarchar(45) NOT NULL,
        [Mac] nchar(12) NOT NULL,
        [ObservedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_ArpEntries] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ArpEntries_Switches_SourceSwitchId] FOREIGN KEY ([SourceSwitchId]) REFERENCES [Switches] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109101636_Phase2Discovery'
)
BEGIN
    CREATE TABLE [DiscoveryJobs] (
        [Id] int NOT NULL IDENTITY,
        [SwitchId] int NOT NULL,
        [Status] int NOT NULL,
        [QueuedAt] datetime2 NOT NULL,
        [StartedAt] datetime2 NULL,
        [CompletedAt] datetime2 NULL,
        [StepsJson] nvarchar(max) NULL,
        [PortsFound] int NOT NULL,
        [DevicesFound] int NOT NULL,
        [NeighborsFound] int NOT NULL,
        [ErrorCount] int NOT NULL,
        [Summary] nvarchar(500) NULL,
        CONSTRAINT [PK_DiscoveryJobs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DiscoveryJobs_Switches_SwitchId] FOREIGN KEY ([SwitchId]) REFERENCES [Switches] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109101636_Phase2Discovery'
)
BEGIN
    CREATE TABLE [SwitchPorts] (
        [Id] int NOT NULL IDENTITY,
        [SwitchId] int NOT NULL,
        [IfIndex] int NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [Descr] nvarchar(255) NULL,
        [Alias] nvarchar(255) NULL,
        [AdminUp] bit NOT NULL,
        [OperUp] bit NOT NULL,
        [SpeedMbps] bigint NOT NULL,
        [PhysAddress] nchar(12) NULL,
        [Mtu] int NOT NULL,
        [Vlan] int NULL,
        [PortType] int NOT NULL,
        [LastSeen] datetime2 NOT NULL,
        CONSTRAINT [PK_SwitchPorts] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SwitchPorts_Switches_SwitchId] FOREIGN KEY ([SwitchId]) REFERENCES [Switches] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109101636_Phase2Discovery'
)
BEGIN
    CREATE TABLE [DiscoveryErrors] (
        [Id] int NOT NULL IDENTITY,
        [JobId] int NOT NULL,
        [Step] nvarchar(100) NOT NULL,
        [Code] nvarchar(100) NOT NULL,
        [Message] nvarchar(1000) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_DiscoveryErrors] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DiscoveryErrors_DiscoveryJobs_JobId] FOREIGN KEY ([JobId]) REFERENCES [DiscoveryJobs] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109101636_Phase2Discovery'
)
BEGIN
    CREATE TABLE [Devices] (
        [Id] int NOT NULL IDENTITY,
        [Mac] nchar(12) NOT NULL,
        [Hostname] nvarchar(255) NOT NULL,
        [HostnameSource] int NOT NULL,
        [HostnameCheckedAt] datetime2 NULL,
        [IpAddress] nvarchar(45) NULL,
        [IpSourceSwitchId] int NULL,
        [IpObservedAt] datetime2 NULL,
        [SwitchId] int NULL,
        [PortId] int NULL,
        [Vlan] int NULL,
        [LowConfidence] bit NOT NULL,
        [FirstSeen] datetime2 NOT NULL,
        [LastSeen] datetime2 NOT NULL,
        CONSTRAINT [PK_Devices] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Devices_SwitchPorts_PortId] FOREIGN KEY ([PortId]) REFERENCES [SwitchPorts] ([Id]),
        CONSTRAINT [FK_Devices_Switches_SwitchId] FOREIGN KEY ([SwitchId]) REFERENCES [Switches] ([Id]) ON DELETE SET NULL
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109101636_Phase2Discovery'
)
BEGIN
    CREATE TABLE [Observations] (
        [Id] bigint NOT NULL IDENTITY,
        [Mac] nchar(12) NOT NULL,
        [SwitchId] int NOT NULL,
        [PortId] int NOT NULL,
        [Vlan] int NULL,
        [IsStatic] bit NOT NULL,
        [FirstSeen] datetime2 NOT NULL,
        [LastSeen] datetime2 NOT NULL,
        CONSTRAINT [PK_Observations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Observations_SwitchPorts_PortId] FOREIGN KEY ([PortId]) REFERENCES [SwitchPorts] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_Observations_Switches_SwitchId] FOREIGN KEY ([SwitchId]) REFERENCES [Switches] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109101636_Phase2Discovery'
)
BEGIN
    CREATE INDEX [IX_ArpEntries_Mac] ON [ArpEntries] ([Mac]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109101636_Phase2Discovery'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ArpEntries_SourceSwitchId_Ip] ON [ArpEntries] ([SourceSwitchId], [Ip]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109101636_Phase2Discovery'
)
BEGIN
    CREATE INDEX [IX_Devices_Hostname] ON [Devices] ([Hostname]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109101636_Phase2Discovery'
)
BEGIN
    CREATE INDEX [IX_Devices_IpAddress] ON [Devices] ([IpAddress]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109101636_Phase2Discovery'
)
BEGIN
    CREATE INDEX [IX_Devices_LastSeen] ON [Devices] ([LastSeen]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109101636_Phase2Discovery'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Devices_Mac] ON [Devices] ([Mac]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109101636_Phase2Discovery'
)
BEGIN
    CREATE INDEX [IX_Devices_PortId] ON [Devices] ([PortId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109101636_Phase2Discovery'
)
BEGIN
    CREATE INDEX [IX_Devices_SwitchId] ON [Devices] ([SwitchId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109101636_Phase2Discovery'
)
BEGIN
    CREATE INDEX [IX_Devices_SwitchId_LastSeen] ON [Devices] ([SwitchId], [LastSeen]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109101636_Phase2Discovery'
)
BEGIN
    CREATE INDEX [IX_DiscoveryErrors_JobId] ON [DiscoveryErrors] ([JobId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109101636_Phase2Discovery'
)
BEGIN
    CREATE INDEX [IX_DiscoveryJobs_QueuedAt] ON [DiscoveryJobs] ([QueuedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109101636_Phase2Discovery'
)
BEGIN
    CREATE INDEX [IX_DiscoveryJobs_SwitchId_Status] ON [DiscoveryJobs] ([SwitchId], [Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109101636_Phase2Discovery'
)
BEGIN
    CREATE INDEX [IX_Observations_LastSeen] ON [Observations] ([LastSeen]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109101636_Phase2Discovery'
)
BEGIN
    CREATE INDEX [IX_Observations_Mac] ON [Observations] ([Mac]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109101636_Phase2Discovery'
)
BEGIN
    CREATE INDEX [IX_Observations_PortId] ON [Observations] ([PortId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109101636_Phase2Discovery'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Observations_SwitchId_Mac] ON [Observations] ([SwitchId], [Mac]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109101636_Phase2Discovery'
)
BEGIN
    CREATE INDEX [IX_SwitchPorts_Name] ON [SwitchPorts] ([Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109101636_Phase2Discovery'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SwitchPorts_SwitchId_IfIndex] ON [SwitchPorts] ([SwitchId], [IfIndex]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109101636_Phase2Discovery'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261109101636_Phase2Discovery', N'8.0.11');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109103131_Phase3Neighbors'
)
BEGIN
    ALTER TABLE [SwitchPorts] ADD [Confidence] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109103131_Phase3Neighbors'
)
BEGIN
    ALTER TABLE [SwitchPorts] ADD [NeighborId] bigint NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109103131_Phase3Neighbors'
)
BEGIN
    CREATE TABLE [Neighbors] (
        [Id] bigint NOT NULL IDENTITY,
        [SwitchId] int NOT NULL,
        [PortId] int NOT NULL,
        [Protocol] int NOT NULL,
        [RemoteKey] nvarchar(300) NOT NULL,
        [ChassisId] nvarchar(255) NULL,
        [ChassisMac] nchar(12) NULL,
        [RemotePortId] nvarchar(255) NULL,
        [RemotePortDescr] nvarchar(255) NULL,
        [SysName] nvarchar(255) NULL,
        [SysDescr] nvarchar(500) NULL,
        [Platform] nvarchar(255) NULL,
        [Caps] int NOT NULL,
        [MgmtIp] nvarchar(45) NULL,
        [MatchedSwitchId] int NULL,
        [Evidence] nvarchar(500) NULL,
        [FirstSeen] datetime2 NOT NULL,
        [LastSeen] datetime2 NOT NULL,
        CONSTRAINT [PK_Neighbors] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Neighbors_SwitchPorts_PortId] FOREIGN KEY ([PortId]) REFERENCES [SwitchPorts] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_Neighbors_Switches_SwitchId] FOREIGN KEY ([SwitchId]) REFERENCES [Switches] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109103131_Phase3Neighbors'
)
BEGIN
    CREATE TABLE [SwitchMacs] (
        [Id] int NOT NULL IDENTITY,
        [SwitchId] int NOT NULL,
        [Mac] nchar(12) NOT NULL,
        CONSTRAINT [PK_SwitchMacs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SwitchMacs_Switches_SwitchId] FOREIGN KEY ([SwitchId]) REFERENCES [Switches] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109103131_Phase3Neighbors'
)
BEGIN
    CREATE INDEX [IX_Neighbors_MatchedSwitchId] ON [Neighbors] ([MatchedSwitchId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109103131_Phase3Neighbors'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Neighbors_PortId_Protocol_RemoteKey] ON [Neighbors] ([PortId], [Protocol], [RemoteKey]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109103131_Phase3Neighbors'
)
BEGIN
    CREATE INDEX [IX_Neighbors_SwitchId] ON [Neighbors] ([SwitchId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109103131_Phase3Neighbors'
)
BEGIN
    CREATE INDEX [IX_SwitchMacs_Mac] ON [SwitchMacs] ([Mac]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109103131_Phase3Neighbors'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SwitchMacs_SwitchId_Mac] ON [SwitchMacs] ([SwitchId], [Mac]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109103131_Phase3Neighbors'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261109103131_Phase3Neighbors', N'8.0.11');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109111248_Phase6Settings'
)
BEGIN
    DECLARE @var0 sysname;
    SELECT @var0 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[SwitchPorts]') AND [c].[name] = N'PhysAddress');
    IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [SwitchPorts] DROP CONSTRAINT [' + @var0 + '];');
    ALTER TABLE [SwitchPorts] ALTER COLUMN [PhysAddress] char(12) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109111248_Phase6Settings'
)
BEGIN
    DROP INDEX [IX_SwitchMacs_Mac] ON [SwitchMacs];
    DROP INDEX [IX_SwitchMacs_SwitchId_Mac] ON [SwitchMacs];
    DECLARE @var1 sysname;
    SELECT @var1 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[SwitchMacs]') AND [c].[name] = N'Mac');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [SwitchMacs] DROP CONSTRAINT [' + @var1 + '];');
    ALTER TABLE [SwitchMacs] ALTER COLUMN [Mac] char(12) NOT NULL;
    CREATE INDEX [IX_SwitchMacs_Mac] ON [SwitchMacs] ([Mac]);
    CREATE UNIQUE INDEX [IX_SwitchMacs_SwitchId_Mac] ON [SwitchMacs] ([SwitchId], [Mac]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109111248_Phase6Settings'
)
BEGIN
    DROP INDEX [IX_Observations_Mac] ON [Observations];
    DROP INDEX [IX_Observations_SwitchId_Mac] ON [Observations];
    DECLARE @var2 sysname;
    SELECT @var2 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Observations]') AND [c].[name] = N'Mac');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [Observations] DROP CONSTRAINT [' + @var2 + '];');
    ALTER TABLE [Observations] ALTER COLUMN [Mac] char(12) NOT NULL;
    CREATE INDEX [IX_Observations_Mac] ON [Observations] ([Mac]);
    CREATE UNIQUE INDEX [IX_Observations_SwitchId_Mac] ON [Observations] ([SwitchId], [Mac]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109111248_Phase6Settings'
)
BEGIN
    DECLARE @var3 sysname;
    SELECT @var3 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Neighbors]') AND [c].[name] = N'ChassisMac');
    IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [Neighbors] DROP CONSTRAINT [' + @var3 + '];');
    ALTER TABLE [Neighbors] ALTER COLUMN [ChassisMac] char(12) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109111248_Phase6Settings'
)
BEGIN
    DROP INDEX [IX_Devices_Mac] ON [Devices];
    DECLARE @var4 sysname;
    SELECT @var4 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Devices]') AND [c].[name] = N'Mac');
    IF @var4 IS NOT NULL EXEC(N'ALTER TABLE [Devices] DROP CONSTRAINT [' + @var4 + '];');
    ALTER TABLE [Devices] ALTER COLUMN [Mac] char(12) NOT NULL;
    CREATE UNIQUE INDEX [IX_Devices_Mac] ON [Devices] ([Mac]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109111248_Phase6Settings'
)
BEGIN
    DROP INDEX [IX_ArpEntries_Mac] ON [ArpEntries];
    DECLARE @var5 sysname;
    SELECT @var5 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[ArpEntries]') AND [c].[name] = N'Mac');
    IF @var5 IS NOT NULL EXEC(N'ALTER TABLE [ArpEntries] DROP CONSTRAINT [' + @var5 + '];');
    ALTER TABLE [ArpEntries] ALTER COLUMN [Mac] char(12) NOT NULL;
    CREATE INDEX [IX_ArpEntries_Mac] ON [ArpEntries] ([Mac]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109111248_Phase6Settings'
)
BEGIN
    CREATE TABLE [AppSettings] (
        [Key] nvarchar(100) NOT NULL,
        [Value] nvarchar(max) NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_AppSettings] PRIMARY KEY ([Key])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109111248_Phase6Settings'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261109111248_Phase6Settings', N'8.0.11');
END;
GO

COMMIT;
GO

