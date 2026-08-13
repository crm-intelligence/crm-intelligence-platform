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

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731105107_AddPolicyAssignmentsAndApplicationAudit'
)
BEGIN
    CREATE TABLE [crm].[ApplicationAuditEvents] (
        [EventId] nvarchar(64) NOT NULL,
        [EventType] nvarchar(128) NOT NULL,
        [Outcome] nvarchar(32) NOT NULL,
        [OccurredAt] datetimeoffset(7) NOT NULL,
        [RequestId] nvarchar(32) NULL,
        [PreviousRequestId] nvarchar(32) NULL,
        [CorrelationId] nvarchar(128) NULL,
        [ActorUserId] nvarchar(36) NULL,
        [TenantId] nvarchar(36) NULL,
        [ReportStatus] nvarchar(64) NULL,
        [ReasonCode] nvarchar(128) NULL,
        CONSTRAINT [PK_ApplicationAuditEvents] PRIMARY KEY ([EventId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731105107_AddPolicyAssignmentsAndApplicationAudit'
)
BEGIN
    CREATE TABLE [crm].[UserDataAccessAssignments] (
        [TenantId] nvarchar(36) NOT NULL,
        [UserId] nvarchar(36) NOT NULL,
        [AllowAllRegions] bit NOT NULL,
        [AllowAllStores] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetimeoffset(7) NOT NULL,
        [UpdatedAt] datetimeoffset(7) NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_UserDataAccessAssignments] PRIMARY KEY ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731105107_AddPolicyAssignmentsAndApplicationAudit'
)
BEGIN
    CREATE TABLE [crm].[UserDataAccessRegions] (
        [TenantId] nvarchar(36) NOT NULL,
        [UserId] nvarchar(36) NOT NULL,
        [RegionCode] nvarchar(128) COLLATE Latin1_General_100_CI_AS NOT NULL,
        CONSTRAINT [PK_UserDataAccessRegions] PRIMARY KEY ([TenantId], [UserId], [RegionCode]),
        CONSTRAINT [FK_UserDataAccessRegions_UserDataAccessAssignments_TenantId_UserId] FOREIGN KEY ([TenantId], [UserId]) REFERENCES [crm].[UserDataAccessAssignments] ([TenantId], [UserId]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731105107_AddPolicyAssignmentsAndApplicationAudit'
)
BEGIN
    CREATE TABLE [crm].[UserDataAccessStores] (
        [TenantId] nvarchar(36) NOT NULL,
        [UserId] nvarchar(36) NOT NULL,
        [StoreId] nvarchar(128) COLLATE Latin1_General_100_CI_AS NOT NULL,
        CONSTRAINT [PK_UserDataAccessStores] PRIMARY KEY ([TenantId], [UserId], [StoreId]),
        CONSTRAINT [FK_UserDataAccessStores_UserDataAccessAssignments_TenantId_UserId] FOREIGN KEY ([TenantId], [UserId]) REFERENCES [crm].[UserDataAccessAssignments] ([TenantId], [UserId]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731105107_AddPolicyAssignmentsAndApplicationAudit'
)
BEGIN
    CREATE INDEX [IX_ApplicationAuditEvents_CorrelationId] ON [crm].[ApplicationAuditEvents] ([CorrelationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731105107_AddPolicyAssignmentsAndApplicationAudit'
)
BEGIN
    CREATE INDEX [IX_ApplicationAuditEvents_EventType_OccurredAt] ON [crm].[ApplicationAuditEvents] ([EventType], [OccurredAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731105107_AddPolicyAssignmentsAndApplicationAudit'
)
BEGIN
    CREATE INDEX [IX_ApplicationAuditEvents_OccurredAt] ON [crm].[ApplicationAuditEvents] ([OccurredAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731105107_AddPolicyAssignmentsAndApplicationAudit'
)
BEGIN
    CREATE INDEX [IX_ApplicationAuditEvents_RequestId_OccurredAt] ON [crm].[ApplicationAuditEvents] ([RequestId], [OccurredAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731105107_AddPolicyAssignmentsAndApplicationAudit'
)
BEGIN
    CREATE INDEX [IX_ApplicationAuditEvents_TenantId_ActorUserId_OccurredAt] ON [crm].[ApplicationAuditEvents] ([TenantId], [ActorUserId], [OccurredAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731105107_AddPolicyAssignmentsAndApplicationAudit'
)
BEGIN
    CREATE INDEX [IX_UserDataAccessAssignments_IsActive_TenantId] ON [crm].[UserDataAccessAssignments] ([IsActive], [TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731105107_AddPolicyAssignmentsAndApplicationAudit'
)
BEGIN
    CREATE INDEX [IX_UserDataAccessAssignments_UpdatedAt] ON [crm].[UserDataAccessAssignments] ([UpdatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731105107_AddPolicyAssignmentsAndApplicationAudit'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260731105107_AddPolicyAssignmentsAndApplicationAudit', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731114800_AddTransactionalOutbox'
)
BEGIN
    CREATE TABLE [crm].[OutboxMessages] (
        [MessageId] nvarchar(64) NOT NULL,
        [MessageType] nvarchar(128) NOT NULL,
        [AggregateId] nvarchar(64) NOT NULL,
        [OccurredAt] datetimeoffset(7) NOT NULL,
        [PayloadJson] nvarchar(max) NOT NULL,
        [Status] nvarchar(32) NOT NULL,
        [AttemptCount] int NOT NULL,
        [NextAttemptAt] datetimeoffset(7) NOT NULL,
        [PublishedAt] datetimeoffset(7) NULL,
        [DeadLetteredAt] datetimeoffset(7) NULL,
        [LockOwner] nvarchar(128) NULL,
        [LockedUntil] datetimeoffset(7) NULL,
        [LastFailureCode] nvarchar(128) NULL,
        [CreatedAt] datetimeoffset(7) NOT NULL,
        [UpdatedAt] datetimeoffset(7) NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_OutboxMessages] PRIMARY KEY ([MessageId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731114800_AddTransactionalOutbox'
)
BEGIN
    CREATE INDEX [IX_OutboxMessages_AggregateId_OccurredAt] ON [crm].[OutboxMessages] ([AggregateId], [OccurredAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731114800_AddTransactionalOutbox'
)
BEGIN
    CREATE INDEX [IX_OutboxMessages_DeadLetteredAt] ON [crm].[OutboxMessages] ([DeadLetteredAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731114800_AddTransactionalOutbox'
)
BEGIN
    CREATE INDEX [IX_OutboxMessages_LockedUntil] ON [crm].[OutboxMessages] ([LockedUntil]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731114800_AddTransactionalOutbox'
)
BEGIN
    CREATE INDEX [IX_OutboxMessages_PublishedAt] ON [crm].[OutboxMessages] ([PublishedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731114800_AddTransactionalOutbox'
)
BEGIN
    CREATE INDEX [IX_OutboxMessages_Status_NextAttemptAt_OccurredAt] ON [crm].[OutboxMessages] ([Status], [NextAttemptAt], [OccurredAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731114800_AddTransactionalOutbox'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260731114800_AddTransactionalOutbox', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731124853_AddQueryExecutionAuditMetadata'
)
BEGIN
    ALTER TABLE [crm].[ApplicationAuditEvents] ADD [DataSource] nvarchar(32) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731124853_AddQueryExecutionAuditMetadata'
)
BEGIN
    ALTER TABLE [crm].[ApplicationAuditEvents] ADD [DurationMilliseconds] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731124853_AddQueryExecutionAuditMetadata'
)
BEGIN
    ALTER TABLE [crm].[ApplicationAuditEvents] ADD [ResultTruncated] bit NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731124853_AddQueryExecutionAuditMetadata'
)
BEGIN
    ALTER TABLE [crm].[ApplicationAuditEvents] ADD [RowCount] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731124853_AddQueryExecutionAuditMetadata'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260731124853_AddQueryExecutionAuditMetadata', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731135536_AddDurableTeamsStateAndNotifications'
)
BEGIN
    CREATE TABLE [crm].[TeamsCardActionSubmissions] (
        [ActionToken] nvarchar(64) NOT NULL,
        [RequestId] nvarchar(32) NOT NULL,
        [ActionType] nvarchar(64) NOT NULL,
        [State] nvarchar(16) NOT NULL,
        [ResultRequestId] nvarchar(32) NULL,
        [LockOwner] nvarchar(256) NULL,
        [LockedUntil] datetimeoffset(7) NULL,
        [CreatedAt] datetimeoffset(7) NOT NULL,
        [UpdatedAt] datetimeoffset(7) NOT NULL,
        [CompletedAt] datetimeoffset(7) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_TeamsCardActionSubmissions] PRIMARY KEY ([ActionToken])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731135536_AddDurableTeamsStateAndNotifications'
)
BEGIN
    CREATE TABLE [crm].[TeamsNotificationDeliveries] (
        [DeliveryId] nvarchar(64) NOT NULL,
        [RequestId] nvarchar(32) NOT NULL,
        [NotificationStatus] nvarchar(64) NOT NULL,
        [ReportUpdatedAt] datetimeoffset(7) NOT NULL,
        [State] nvarchar(16) NOT NULL,
        [AttemptCount] int NOT NULL,
        [LockOwner] nvarchar(256) NULL,
        [LockedUntil] datetimeoffset(7) NULL,
        [DeliveredAt] datetimeoffset(7) NULL,
        [CreatedAt] datetimeoffset(7) NOT NULL,
        [UpdatedAt] datetimeoffset(7) NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_TeamsNotificationDeliveries] PRIMARY KEY ([DeliveryId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731135536_AddDurableTeamsStateAndNotifications'
)
BEGIN
    CREATE TABLE [crm].[TeamsNotificationTargets] (
        [RequestId] nvarchar(32) NOT NULL,
        [ConversationId] nvarchar(512) NOT NULL,
        [CreatedAt] datetimeoffset(7) NOT NULL,
        [UpdatedAt] datetimeoffset(7) NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_TeamsNotificationTargets] PRIMARY KEY ([RequestId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731135536_AddDurableTeamsStateAndNotifications'
)
BEGIN
    CREATE INDEX [IX_TeamsNotificationDeliveries_RequestId_ReportUpdatedAt] ON [crm].[TeamsNotificationDeliveries] ([RequestId], [ReportUpdatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731135536_AddDurableTeamsStateAndNotifications'
)
BEGIN
    CREATE INDEX [IX_TeamsNotificationDeliveries_State_LockedUntil] ON [crm].[TeamsNotificationDeliveries] ([State], [LockedUntil]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260731135536_AddDurableTeamsStateAndNotifications'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260731135536_AddDurableTeamsStateAndNotifications', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805100943_AddApplicationAuditMetadata'
)
BEGIN
    ALTER TABLE [crm].[ApplicationAuditEvents] ADD [AuditMetadataJson] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805100943_AddApplicationAuditMetadata'
)
BEGIN
    EXEC(N'ALTER TABLE [crm].[ApplicationAuditEvents] ADD CONSTRAINT [CK_ApplicationAuditEvents_AuditMetadataJson_IsJson] CHECK ([AuditMetadataJson] IS NULL OR ISJSON([AuditMetadataJson]) = 1)');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805100943_AddApplicationAuditMetadata'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260805100943_AddApplicationAuditMetadata', N'10.0.10');
END;

COMMIT;
GO

