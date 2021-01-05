CREATE TABLE "CDR" (
    "ID" uniqueidentifier NOT NULL CONSTRAINT "PK_CDR" PRIMARY KEY,
    "Inserted" datetime NOT NULL,
    "Direction" varchar(3) NOT NULL,
    "Created" datetime NOT NULL,
    "DstUser" varchar(128) NULL,
    "DstHost" varchar(128) NOT NULL,
    "DstUri" varchar(1024) NOT NULL,
    "FromUser" varchar(128) NULL,
    "FromName" varchar(128) NULL,
    "FromHeader" varchar(1024) NULL,
    "CallID" varchar(256) NOT NULL,
    "LocalSocket" varchar(64) NULL,
    "RemoteSocket" varchar(64) NULL,
    "BridgeID" uniqueidentifier NULL,
    "InProgressAt" datetime NULL,
    "InProgressStatus" int NULL,
    "InProgressReason" varchar(512) NULL,
    "RingDuration" int NULL,
    "AnsweredAt" datetime NULL,
    "AnsweredStatus" int NULL,
    "AnsweredReason" varchar(512) NULL,
    "Duration" int NULL,
    "HungupAt" datetime NULL,
    "HungupReason" varchar(512) NULL
);

CREATE TABLE "SIPDomains" (
    "ID" uniqueidentifier NOT NULL CONSTRAINT "PK_SIPDomains" PRIMARY KEY,
    "Domain" nvarchar(128) NOT NULL,
    "AliasList" nvarchar(1024) NULL,
    "Inserted" datetime NOT NULL DEFAULT ((sysdatetime()))
);

CREATE TABLE "SIPCalls" (
    "ID" uniqueidentifier NOT NULL CONSTRAINT "PK_SIPCalls" PRIMARY KEY,
    "CDRID" uniqueidentifier NULL,
    "LocalTag" varchar(64) NOT NULL,
    "RemoteTag" varchar(64) NOT NULL,
    "CallID" varchar(128) NOT NULL,
    "CSeq" int NOT NULL,
    "BridgeID" uniqueidentifier NOT NULL,
    "RemoteTarget" varchar(256) NOT NULL,
    "LocalUserField" varchar(512) NOT NULL,
    "RemoteUserField" varchar(512) NOT NULL,
    "ProxySIPSocket" varchar(64) NULL,
    "RouteSet" varchar(512) NULL,
    "CallDurationLimit" int NULL,
    "Direction" varchar(3) NOT NULL,
    "Inserted" datetime NOT NULL,
    CONSTRAINT "FK__SIPCalls__CDRID__46B27FE2" FOREIGN KEY ("CDRID") REFERENCES "CDR" ("ID") ON DELETE CASCADE
);

CREATE TABLE "SIPAccounts" (
    "ID" uniqueidentifier NOT NULL CONSTRAINT "PK_SIPAccounts" PRIMARY KEY,
    "DomainID" uniqueidentifier NOT NULL,
    "SIPUsername" nvarchar(32) NOT NULL,
    "SIPPassword" nvarchar(32) NOT NULL,
    "IsDisabled" bit NOT NULL,
    "Inserted" datetime NOT NULL DEFAULT ((sysdatetime())),
    CONSTRAINT "FK__SIPAccoun__Domai__1AD3FDA4" FOREIGN KEY ("DomainID") REFERENCES "SIPDomains" ("ID") ON DELETE CASCADE
);

CREATE TABLE "SIPRegistrarBindings" (
    "ID" uniqueidentifier NOT NULL CONSTRAINT "PK_SIPRegistrarBindings" PRIMARY KEY,
    "SIPAccountID" uniqueidentifier NOT NULL,
    "UserAgent" nvarchar(1024) NULL,
    "ContactURI" nvarchar(767) NOT NULL,
    "MangledContactURI" varchar(767) NULL,
    "Expiry" int NOT NULL,
    "RemoteSIPSocket" nvarchar(64) NOT NULL,
    "ProxySIPSocket" nvarchar(64) NULL,
    "RegistrarSIPSocket" nvarchar(64) NULL,
    "LastUpdate" datetime NOT NULL,
    "ExpiryTime" datetime NOT NULL,
    CONSTRAINT "FK__SIPRegist__SIPAc__1DB06A4F" FOREIGN KEY ("SIPAccountID") REFERENCES "SIPAccounts" ("ID") ON DELETE CASCADE
);

CREATE INDEX "IX_SIPAccounts_DomainID" ON "SIPAccounts" ("DomainID");

CREATE UNIQUE INDEX "UQ__SIPAccou__6E36B5B5E2EC7FF1" ON "SIPAccounts" ("SIPUsername", "DomainID");

CREATE INDEX "IX_SIPCalls_CDRID" ON "SIPCalls" ("CDRID");

CREATE UNIQUE INDEX "UQ__SIPDomai__FD349E53D9BC0D1B" ON "SIPDomains" ("Domain");

CREATE INDEX "IX_SIPRegistrarBindings_SIPAccountID" ON "SIPRegistrarBindings" ("SIPAccountID");

COMMIT;