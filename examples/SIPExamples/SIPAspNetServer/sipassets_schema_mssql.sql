begin transaction;

create table SIPDomains
(
 ID uniqueidentifier not null,
 Domain nvarchar(128) not null,			-- The domain name.
 AliasList nvarchar(1024),				-- If not null indicates a semi-colon delimited list of aliases for the domain.
 Inserted datetime not null default sysdatetime(),
 Primary Key(ID),
 Unique(domain)
);

create table SIPDialPlans
(
 ID uniqueidentifier not null,
 DialPlanName varchar(64) not null,			-- Name the owner has assigned to the dialplan to allow them to choose between their different ones.
 DialPlanScript varchar(max),
 Inserted datetime not null,
 LastUpdate datetime not null,
 AcceptNonInvite bit not null default 0,	-- If true the dialplan will accept non-INVITE requests.
 Primary Key(ID),
 Unique(DialPlanName)
);

create table SIPAccounts
(
 ID uniqueidentifier not null,
 DomainID uniqueidentifier not null,
 SIPDialPlanID uniqueidentifier null,
 SIPUsername nvarchar(32) not null,
 SIPPassword nvarchar(32) not null,
 IsDisabled bit not null default 0,
 Inserted datetime not null default sysdatetime(),
 Primary Key(ID),
 Foreign Key(DomainID) references SIPDomains(ID) on delete cascade on update cascade,
 Foreign Key(SIPDialPlanID) references SIPDialPlans(ID) on delete cascade on update cascade,
 Unique(SIPUsername, DomainID)
);

create table SIPRegistrarBindings
(
 ID uniqueidentifier not null,				-- A unique id assigned to the binding in the Registrar.
 SIPAccountID uniqueidentifier not null,
 UserAgent nvarchar(1024),
 ContactURI nvarchar(767) not null,			-- This is the URI the user agent sent in its Contact header requesting a binding for.
 MangledContactURI varchar(767),			-- The is the URI the Registrar deemed in its wisdom was the binding the user agent really wanted set (wisdom=try and cope with NAT).
 Expiry int not null,
 RemoteSIPSocket nvarchar(64) not null,
 ProxySIPSocket nvarchar(64),
 RegistrarSIPSocket nvarchar(64) null,
 LastUpdate datetime not null,
 ExpiryTime datetime not null,
 Primary Key(ID),
 Foreign Key(SIPAccountID) references SIPAccounts(ID) on delete cascade on update cascade,
);

create table CDR
(
 ID uniqueidentifier not null,
 Inserted datetime not null,
 Direction varchar(3) not null,					-- In or Out with respect to the proxy.
 Created datetime not null,				-- Time the cdr was created by the proxy.
 DstUser varchar(128),							-- The user portion of the destination URI.
 DstHost varchar(128) not null,					-- The host portion of the destination URI.
 DstUri varchar(1024) not null,					-- The full destination URI.
 FromUser varchar(128),							-- The user portion of the From header URI.
 FromName varchar(128),							-- The name portion of the From header.
 FromHeader varchar(1024),						-- The full From header.
 CallID varchar(256) not null,					-- The Call-ID of the call.
 LocalSocket varchar(64) null,				    -- The socket on the proxy used for the call.
 RemoteSocket varchar(64) null,				    -- The remote socket used for the call.
 BridgeID uniqueidentifier null,   			    -- If the call was involved in a bridge the id of it.
 InProgressAt datetime null default null, -- The time of the last info response for the call.
 InProgressStatus int,							-- The SIP response status code of the last info response for the call.
 InProgressReason varchar(512),					-- The SIP response reason phrase of the last info response for the call.
 RingDuration int,								-- Number of seconds the call was ringing for.
 AnsweredAt datetime null default null,	-- The time the call was answered with a final response.
 AnsweredStatus int,							-- The SIP response status code of the final response for the call.
 AnsweredReason varchar(512),					-- The SIP response reason phrase of the final response for the call.
 Duration int,									-- Number of seconds the call was established for.
 HungupAt datetime null default null,	    -- The time the call was hungup.
 HungupReason varchar(512),						-- The SIP response Reason header on the BYE request if present.
 Primary Key(ID)
);

create table SIPCalls
(
 ID uniqueidentifier not null,
 CDRID uniqueidentifier null,
 LocalTag varchar(64) not null,
 RemoteTag varchar(64) not null,
 CallID varchar(128) not null,
 CSeq int not null,
 BridgeID uniqueidentifier not null,
 RemoteTarget varchar(256) not null,
 LocalUserField varchar(512) not null,
 RemoteUserField varchar(512) not null,
 ProxySIPSocket varchar(64),
 RouteSet varchar(512),
 CallDurationLimit int,
 Direction varchar(3) not null,					-- In or Out with respect to the proxy.
 Inserted datetime not null,
 Primary Key(ID),
 Foreign Key(CDRID) references CDR(ID) on delete cascade on update cascade
);

commit;
