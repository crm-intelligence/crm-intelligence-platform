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
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731083646_InitialCrmPersistence'
)
BEGIN
    IF SCHEMA_ID(N'crm') IS NULL EXEC(N'CREATE SCHEMA [crm];');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731083646_InitialCrmPersistence'
)
BEGIN
    CREATE TABLE [crm].[Conversations] (
        [Id] nvarchar(32) NOT NULL,
        [TeamsConversationId] nvarchar(512) COLLATE Latin1_General_100_CI_AS NOT NULL,
        [UserId] nvarchar(36) NOT NULL,
        [TenantId] nvarchar(36) NOT NULL,
        [LastRequestId] nvarchar(32) NOT NULL,
        [CreatedAt] datetimeoffset(7) NOT NULL,
        [UpdatedAt] datetimeoffset(7) NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Conversations] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731083646_InitialCrmPersistence'
)
BEGIN
    CREATE TABLE [crm].[ReportRequests] (
        [RequestId] nvarchar(32) NOT NULL,
        [UserId] nvarchar(36) NOT NULL,
        [TenantId] nvarchar(36) NOT NULL,
        [ConversationId] nvarchar(512) NOT NULL,
        [PreviousRequestId] nvarchar(32) NULL,
        [Prompt] nvarchar(2000) NOT NULL,
        [Status] nvarchar(64) NOT NULL,
        [CreatedAt] datetimeoffset(7) NOT NULL,
        [UpdatedAt] datetimeoffset(7) NOT NULL,
        [ReferenceDate] date NOT NULL,
        [CorrelationId] nvarchar(128) NOT NULL,
        [ReportId] nvarchar(256) NULL,
        [PowerBiUrl] nvarchar(2048) NULL,
        [Summary] nvarchar(4000) NULL,
        [ErrorCode] nvarchar(128) NULL,
        [ErrorMessage] nvarchar(1000) NULL,
        [ClarificationQuestion] nvarchar(1000) NULL,
        [ClarificationResponse] nvarchar(2000) NULL,
        [CanonicalRequestJson] nvarchar(max) NULL,
        [RejectionCode] nvarchar(128) NULL,
        [RejectionMessage] nvarchar(1000) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_ReportRequests] PRIMARY KEY ([RequestId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731083646_InitialCrmPersistence'
)
BEGIN
    CREATE INDEX [IX_Conversations_LastRequestId] ON [crm].[Conversations] ([LastRequestId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731083646_InitialCrmPersistence'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Conversations_TeamsConversationId] ON [crm].[Conversations] ([TeamsConversationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731083646_InitialCrmPersistence'
)
BEGIN
    CREATE INDEX [IX_Conversations_TenantId_UserId_TeamsConversationId] ON [crm].[Conversations] ([TenantId], [UserId], [TeamsConversationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731083646_InitialCrmPersistence'
)
BEGIN
    CREATE INDEX [IX_Conversations_TenantId_UserId_UpdatedAt] ON [crm].[Conversations] ([TenantId], [UserId], [UpdatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731083646_InitialCrmPersistence'
)
BEGIN
    CREATE INDEX [IX_ReportRequests_ConversationId_CreatedAt] ON [crm].[ReportRequests] ([ConversationId], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731083646_InitialCrmPersistence'
)
BEGIN
    CREATE INDEX [IX_ReportRequests_PreviousRequestId] ON [crm].[ReportRequests] ([PreviousRequestId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731083646_InitialCrmPersistence'
)
BEGIN
    CREATE INDEX [IX_ReportRequests_Status_UpdatedAt] ON [crm].[ReportRequests] ([Status], [UpdatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731083646_InitialCrmPersistence'
)
BEGIN
    CREATE INDEX [IX_ReportRequests_TenantId_UserId_ConversationId_CreatedAt] ON [crm].[ReportRequests] ([TenantId], [UserId], [ConversationId], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731083646_InitialCrmPersistence'
)
BEGIN
    CREATE INDEX [IX_ReportRequests_TenantId_UserId_RequestId] ON [crm].[ReportRequests] ([TenantId], [UserId], [RequestId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731083646_InitialCrmPersistence'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260731083646_InitialCrmPersistence', N'10.0.10');
END;

COMMIT;
GO

