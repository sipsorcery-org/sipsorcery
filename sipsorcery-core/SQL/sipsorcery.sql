-- =======
-- SIP Switch
--======

/*create table admingroups
(
	admingroupid varchar(36) not null,
	admingroupname varchar(32) not null,
	inserted timestamptz not null default now(),
	Primary Key (admingroupid),
	Unique(admingroupname)
);

create table admingroupmembers
(
	admingroupmemberid varchar(36) not null,
	admingroupid varchar(36) not null,
	username varchar(32) not null,
	Primary Key(admingroupmemberid),
	Foreign Key(admingroupid) references admingroups(admingroupid) on delete cascade on update cascade,
	Unique(admingroupid, username)
);*/

create table sipdomains
(
	domainid varchar(36) not null,
	domain varchar(128) not null,
	owner varchar(32) not null,				-- The username of the person the extension is allocated to.
	inserted timestamptz not null default now(),
	Primary Key(domainid),
	Foreign Key(owner) references customers(username) on delete cascade on update cascade,
	Unique(domain)
);

create table domainaliases
(
	domainaliasid varchar(36) not null,
	domainid varchar(36) not null,
	domainalias varchar(128) not null,
	inserted timestamptz not null default now(),
	Primary Key(domainaliasid),
	Foreign Key(domainid) references domains(domainid),
	Unique(domainalias)
);

create table sipaccounts
(
	sipaccountid varchar(36) not null,
	sipusername varchar(32),
	sippassword varchar(32) not null,
	owner varchar(32) not null,				-- The username of the person the extension is allocated to.
	domainid varchar(36) not null,			-- The domain the SIP account belongs to.
	dialplanname varchar(64),
	inserted timestamptz not null default now(),
	Primary Key(sipaccountid),
	Foreign Key(owner) references customers(username) on delete cascade on update cascade,
	Foreign Key(domainid) references sipdomains(domainid)  on delete cascade on update cascade,
	Unique(sipusername, domainid)
);

create table sipaccountcontacts
(
	contactid varchar(36) not null,				-- A unique id assigned to the binding in the Registrar.
	sipaccountid varchar(36) not null,
	contacturi varchar(1024) not null,			-- The is the URI the Registrar deemed in its wisdom was the binding the user agent really wanted set (wisdom=try and cope with NAT).
	requestedcontacturi varchar(1024) not null,	-- This is the URI the user agent sent in its Contact header requesting a binding for.
	useragent varchar(1024),
	proxysocket varchar(21),
	proxyprotocol varchar(3),					-- udp or tcp. Used for nat keep-alives. 
	expiresat timestamptz not null default now(),
	lastupdate timestamptz not null default now(),
	inserted timestamptz not null default now(),
	Primary Key(contactid),
	Foreign Key(sipaccountid) references sipaccounts(sipaccountid) on delete cascade,
	Unique(sipaccountid, contacturi)
);

create table extensions
(
	extensionid varchar(36) not null,
	extension varchar(32) not null unique,	-- Arbitrary SIP URI for incoming calls only.
	owner varchar(32) not null,				-- The username of the person the extension is allocated to.
	domainid varchar(36) not null,			-- The domain the extension belongs to.
	sipaccountid varchar(36),				-- If this is non-null calls to this extension will be forwarded directly to the SIP account, otherwise the dialplan will be used.
	Primary Key(extensionid),
	Foreign Key(owner) references customers(username) on delete cascade on update cascade,
	Foreign Key(domainid) references domains(domainid)  on delete cascade on update cascade,
	Foreign Key(sipaccountid) references sipaccounts(sipaccountid),
	Unique(extension, domainid)
);

create table customers
(
 customerid varchar(36) NOT NULL unique,
 firstname varchar(255) not null,
 lastname varchar(255) not null,
 initial varchar(10),
 username varchar(32) not null unique,
 password varchar(32) not null,
 companyname varchar(255),
 broadbandprovider varchar(255),
 whereheardabout varchar(255),
 emailaddress varchar(255) not null,		/* If the customer was referred the code of the referrer. */
 unsubscribed bit not null default '0',		/* Set to 0 if the customer has sent an unsusbcribe request for us to not send them any emails. */
 active bit not null default '1',			/* Whether this account has been used in the last month (or specified period). */
 suspended bit not null default '0',		/* Whether this account has been suspended. If so it will not be authorised for logins. */
 securityquestion varchar(1024),
 securityanswer varchar(256),
 inserted timestamptz NOT NULL default now(),
 Primary Key(CustomerId)
);

create index customers_custid_index on customers(customerid);
create index customers_lastname_index on customers(lastname);
create index customers_username_index on customers(username);

create table sipproviders
(
	providerid varchar(36) not null,
	owner varchar(36) not null,
	providername varchar(50)not null,
	providerusername varchar(32) not null,
	providerpassword varchar(32),
	providerserver varchar(256) not null,
	provideroutboundproxy varchar(256),
	providerfrom varchar(256),
	providercustom varchar(1024),
	registercontact varchar(256),
	registerexpiry int not null default 3600,
	registerserver varchar(256),
	registerauthusername varchar(32),
	registerrealm varchar(256),
	registerenabled bit not null default '0',
	registerenabledadmin bit not null default '1',		-- This allows an admin to disable the registration and override the user.
	registerdisabledreason varchar(256),				-- If a registration has been disabled by the RegistrationAgent the reason will be specified here. Examples are 403 Forbidden responses.
	lastupdate timestamptz not null default now(),
	inserted timestamptz not null default now(),
	Primary Key(providerid),
	Foreign Key(owner) references Customers(username) on delete cascade on update cascade,
	Unique(owner, providername)
);

-- There is a 1-to-1 relationship between sipproviders and sipprovidercontacts at the time of writing. The SIPRegsitrationAgent only ever manages one contact
-- per SIPProvider so that's all the db is required to handle. The tables are split for functional separation and in case the registration agent is ever changed
-- to register multiple contacts per provider.

create table sipprovidercontacts
(
	providercontactid varchar(36) not null,
	providerid varchar(36) not null,
	registeredcontact varchar(128),
	protocol varchar(3),						-- Whether contact is being registered over TCP or UDP.
	failuremessage varchar(256),				-- If the registration has failed with an error response or reason.
	expiry int,									-- If the contact is registered the expiry in seconds the server responded with.
	lastregistersent timestamptz,				-- The time the last registration request was sent.
	nextregister timestamptz,					-- If the contact is not registered this is the time the next registration attempt is due.
	retryinterval int default 90,				-- If the contact is not registered this is the interval in seconds being used for retries.
	registrationagentserver varchar(32),		-- The address of the registration agent server that last updated this record.
	inserted timestamptz not null default now(),
	Primary Key(providercontactid),
	Foreign Key(providerid) references sipproviders(providerid) on delete cascade on update cascade
);

create table dialplans
(
	dialplanid varchar(36) not null,
	owner varchar(32) not null,
	dialplanname varchar(64) not null default 'default',	-- Name the owner has assigned to the dialplan to allow them to choose between their different ones.
	dialplanscript varchar(10000),
	scripttype varchar(12) not null default 'Ruby',			-- The type of script the dialplan has, supported values are: Asterisk, Ruby, Python and JScript.
	--updated bit not null default '0',						-- 1 indicates the dial plan has been updated and the SIP Proxy needs to reload.
	--useforincoming bit not null default '0',				-- 1 inidcates the user wishes to use the dial plan for incoming calls as well as outgoing.
	lastupdate timestamptz not null default now(),
	inserted timestamptz not null default now(),
	Primary Key(dialplanid),
	Foreign Key(owner) references Customers(username) on delete cascade on update cascade
);

create table switchlog
(
	logid serial,
	username varchar(32),
	logmessage varchar(512) not null,
	inserted timestamptz not null default now(),
	Primary Key(logid)
);

create table AuthenticatedSessions
(
 sessionid varchar(36) not null unique,
 username varchar(32) not null,
 ipaddress varchar(15),
 starttime timestamptz not null default now(),
 lastcontacttime timestamptz not null default now(),
 expired bit not null default '0',
 Primary Key(sessionid),
 Foreign Key(username) references Customers(username) on delete cascade
);

create table cdr
(
 cdrid varchar(36) not null unique,
 owner varchar(36),
 direction varchar(3) not null,					/* In or Out with respect to the proxy. */
 created timestamptz NOT NULL default now(),	/* Time the cdr was created by the proxy. */
 dst varchar(128) not null,						/* The user portion of the destination URI. */
 dsthost varchar(128) not null,					/* The host portion of the destination URI. */
 dsturi varchar(1024) not null,					/* The full destination URI. */
 fromuser varchar(128),							/* The user portion of the From header URI. */
 fromname varchar(128),							/* The name portion of the From header. */
 "from" varchar(1024) not null,					/* The full From header. */
 callid varchar(256) not null,					/* The Call-ID of the call. */
 localsocket varchar(23) not null,				/* The socket on the proxy used for the call. */
 remotesocket varchar(23) not null,				/* The remote socket used for the call. */
 bridgeid varchar(36),							/* If the call was involved in a bridge the id of it. */
 inprogresstime timestamptz,					/* The time of the last info response for the call. */
 inprogressstatus int,							/* The SIP response status code of the last info response for the call. */
 inprogressreason varchar(64),					/* The SIP response reason phrase of the last info response for the call. */
 ringingduration int,							/* Number of seconds the call was ringing for. */
 answeredtime timestamptz,						/* The time the call was answered with a final response. */
 answeredstatus int,							/* The SIP response status code of the final response for the call. */
 answeredreason varchar(64),					/* The SIP response reason phrase of the final response for the call. */
 duration int,									/* Number of seconds the call was established for. */
 hunguptime timestamptz,						/* The time the call was hungup. */
 hungupreason varchar(64),						/* The SIP response Reason header on the BYE request if present. */
 inserted timestamptz not null default now(),
 Primary Key(cdrid),
 Foreign Key(owner) references Customers(username) on delete cascade
);

-- V2
create table customers
(
 customerid varchar(36) not null,
 customerusername varchar(32) not null,
 customerpassword varchar(32) not null,
 Primary Key(customerid)
);