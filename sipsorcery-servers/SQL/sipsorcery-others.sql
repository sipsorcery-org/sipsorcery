use sipsorcery;

-- Maps to class SIPSorcery.CRM.Customer.
create table customers
(
 id varchar(36) not null,
 customerusername varchar(32) not null,
 customerpassword varchar(32) not null,
 emailaddress varchar(255) not null,
 firstname varchar(64),
 lastname varchar(64),
 city varchar(64),
 country varchar(64),
 website varchar(256),		
 active bit not null default 1,			-- Whether this account has been used in the last month (or specified period). 
 suspended bit not null default 0,		-- Whether this account has been suspended. If so it will not be authorised for logins. 
 securityquestion varchar(1024),
 securityanswer varchar(256),
 createdfromipaddress varchar(15),
 adminid varchar(32),						-- Like a whitelabelid. If set identifies this user as the administrative owner of all accounts that have the same value for their adminmemberid.
 adminmemberid varchar(32),					-- If set it designates this customer as a belonging to the administrative domain of the customer with the same adminid.
 maxexecutioncount int not null,	     	-- The mamimum number of simultaneous executions of the customer's dialplans that are permitted.
 executioncount int not null,				-- The current number of dialplan executions in progress.
 authorisedapps varchar(2048),				-- A semi-colon delimited list of privileged apps that this customer's dialplan are authorised to use.
 timezone varchar(128),
 emailaddressconfirmed bit not null default 0,
 inserted varchar(33) not null,
 suspendedreason varchar(1024) null,
 invitecode varchar(36) null,
 passwordresetid varchar(36) null,
 passwordresetidsetat varchar(33) null,			-- Time the password reset id was generated at.
 usernamerecoveryidsetat varchar(33) null,		-- Time the username recovery id was generated at.
 usernamerecoveryfailurecount int null,			-- Number of failed attempts at answering the security question when attempting a username recovery.
 usernamerecoverylastattemptat varchar(33) null,-- Time the last username recovery was attempted at.
 apikey varchar(96) null,
 usernamerecoveryid varchar(36) null,
 servicelevel varchar(64) not null default 'Free',
 servicerenewaldate varchar(33) null,
 Primary Key(id),
 Unique(customerusername)
);

-- Maps to class SIPSorcery.CRM.CustomerSession.
create table customersessions
(
 id varchar(36) not null,
 sessionid varchar(96) not null,
 customerusername varchar(32) not null,
 inserted varchar(33) not null,
 expired bit not null default 0,
 ipaddress varchar(45),
 timelimitminutes int not null default 60,
 Primary Key(id),
 Foreign Key(customerusername) references customers(customerusername) on delete cascade
);

-- Maps to class SIPSorcery.SIP.App.SIPDomain.
create table sipdomains
(
 id varchar(36) not null,
 domain varchar(128) not null,			-- The domain name.
 aliaslist varchar(1024),				-- If not null indicates a semi-colon delimited list of aliases for the domain.
 owner varchar(32),						-- The username of the customer that owns the domain. If null it's a public domain.
 inserted varchar(33) not null,
 Primary Key(id),
 Foreign Key(owner) references customers(customerusername),
 Unique(domain)
);

-- Maps to class SIPSorcery.SIP.App.SIPAccount.
create table sipaccounts
(
 id varchar(36) not null,
 sipusername varchar(32) not null,
 sippassword varchar(32) not null,
 owner varchar(32) not null,					-- The username of the customer that owns the domain.
 adminmemberid varchar(32),
 sipdomain varchar(128) not null,				-- The domain the SIP account belongs to.
 sendnatkeepalives bit not null default 1,
 isincomingonly bit not null default 0,
 outdialplanname varchar(64),
 indialplanname varchar(64),
 isuserdisabled bit not null default 0,
 isadmindisabled bit not null default 0,
 admindisabledreason varchar(256),
 networkid varchar(16),
 ipaddressacl varchar(256),
 inserted varchar(33) not null,
 isswitchboardenabled bit not null default 1,
 dontmangleenabled bit not null default 0,
 avatarurl varchar(1024) null,					-- URL that points to an image that can be displayed in user interfaces.
 Primary Key(id),
 Foreign Key(owner) references customers(customerusername) on delete cascade on update cascade,
 Foreign Key(sipdomain) references sipdomains(domain) on delete cascade on update cascade,
 Unique(sipusername, sipdomain)
);

-- Maps to class SIPSorcery.SIP.App.SIPRegistrarBinding.
create table sipregistrarbindings
(
 id varchar(36) not null,					-- A unique id assigned to the binding in the Registrar.
 sipaccountid varchar(36) not null,
 sipaccountname varchar(160) not null,		-- Used for information only, allows quick visibility on which SIP account the binding is for.
 owner varchar(32) not null,				-- The username of the customer that owns the domain.
 adminmemberid varchar(32),
 useragent varchar(1024),
 contacturi varchar(767) not null,			-- This is the URI the user agent sent in its Contact header requesting a binding for.
 mangledcontacturi varchar(767),			-- The is the URI the Registrar deemed in its wisdom was the binding the user agent really wanted set (wisdom=try and cope with NAT).
 expiry int not null,
 remotesipsocket varchar(64) not null,
 proxysipsocket varchar(64),
 registrarsipsocket varchar(64) not null,
 lastupdate varchar(33) not null,
 expirytime varchar(33) not null,
 Primary Key(id),
 Foreign Key(sipaccountid) references sipaccounts(id) on delete cascade on update cascade,
 Foreign Key(owner) references customers(customerusername) 
);

-- Maps to class SIPSorcery.SIP.App.SIPProvider.
create table sipproviders
(
 id varchar(36) not null,
 owner varchar(32) not null,
 adminmemberid varchar(32),
 providername varchar(50) not null,
 providerusername varchar(32) not null,
 providerpassword varchar(32),
 providerserver varchar(256) null,
 providerauthusername varchar(32),
 provideroutboundproxy varchar(256),
 providerfrom varchar(256),
 customheaders varchar(1024),
 registercontact varchar(256),
 registerexpiry int,
 registerserver varchar(256),
 registerrealm varchar(256),
 registerenabled bit not null default 0,
 registeradminenabled bit not null default 1,		-- This allows an admin to disable the registration and override the user.
 registerdisabledreason varchar(256),				-- If a registration has been disabled by the RegistrationAgent the reason will be specified here. Examples are 403 Forbidden responses.
 inserted varchar(33) not null,
 lastupdate varchar(33) not null,
 providertype varchar(16) not null default 'sip',
 gvcallbacknumber varchar(16) null,
 gvcallbackpattern varchar(32) null,
 gvcallbacktype varchar(16) null,
 isreadonly bit not null default 0,
 Primary Key(id),
 Foreign Key(owner) references customers(customerusername) on delete cascade on update cascade,
 Unique(owner, providername)
);

-- Maps to class SIPSorcery.SIP.App.SIPProviderBinding.
create table sipproviderbindings
(
 id varchar(36) not null,
 providerid varchar(36) not null,
 providername varchar(50) not null,
 owner varchar(32) not null,
 adminmemberid varchar(32),
 registrationfailuremessage varchar(1024),
 nextregistrationtime varchar(33) not null,
 lastregistertime varchar(33) null default null,
 lastregisterattempt varchar(33) null default null,
 isregistered bit not null default 0,
 bindingexpiry int not null default 3600,
 bindinguri varchar(256) not null,
 registrarsipsocket varchar(256),
 cseq int not null,
 Primary Key(id),
 Foreign Key(owner) references customers(customerusername) on delete cascade on update cascade,
 Foreign Key(providerid) references sipproviders(id) on delete cascade on update cascade
);

-- Maps to class SIPSorcery.SIP.SIPDialPlan.
create table sipdialplans
(
 id varchar(36) not null,
 owner varchar(32) not null,
 adminmemberid varchar(32),
 dialplanname varchar(64) not null default 'default',			-- Name the owner has assigned to the dialplan to allow them to choose between their different ones.
 traceemailaddress varchar(256),
 dialplanscript text,
 scripttypedescription varchar(12) not null default 'Ruby',		-- The type of script the dialplan has, supported values are: Asterisk, Ruby, Python and JScript.
 inserted varchar(33) not null,
 lastupdate varchar(33) not null,
 maxexecutioncount int not null,								-- The maximum number of simultaneous executions of the dialplan that are permitted.
 executioncount int not null,									-- The current number of dialplan executions in progress.
 authorisedapps varchar(2048),									-- A semi-colon delimited list of privileged apps that this dialplan is authorised to use.
 acceptnoninvite bit not null default 0,						-- If true the dialplan will accept non-INVITE requests.
 isreadonly bit not null default 0,
 Primary Key(id),
 Foreign Key(owner) references customers(customerusername) on delete cascade on update cascade,
 Unique(owner, dialplanname)
);

-- Maps to class SIPSorcery.SIP.SIPDialogueAsset.
create table sipdialogues
(
 id varchar(36) not null,
 owner varchar(32) not null,
 adminmemberid varchar(32),
 localtag varchar(64) not null,
 remotetag varchar(64) not null,
 callid varchar(128) not null,
 cseq int not null,
 bridgeid varchar(36) not null,
 remotetarget varchar(256) not null,
 localuserfield varchar(512) not null,
 remoteuserfield varchar(512) not null,
 proxysipsocket varchar(64),
 routeset varchar(512),
 cdrid varchar(36) not null,
 calldurationlimit int,
 inserted varchar(33) not null,
 hangupat varchar(33) null default null,
 transfermode varchar(16),
 direction varchar(3) not null,					-- In or Out with respect to the proxy.
 sdp varchar(2048),
 remotesdp varchar(2048),
 switchboarddescription varchar(1024),
 switchboardcallerdescription varchar(1024),
 SwitchboardOwner varchar(1024),
 SwitchboardLineName varchar(128),
 CRMPersonName nvarchar(256) NULL,
 CRMCompanyName nvarchar(256) NULL,
 CRMPictureURL nvarchar(1024) NULL,
 Primary Key(id),
 Foreign Key(owner) references Customers(customerusername) on delete cascade on update cascade
);

-- Maps to class SIPSorcery.SIP.App.SIPCDRAsset.
create table cdr
(
 id varchar(36) not null,
 owner varchar(32),
 adminmemberid varchar(32),
 inserted varchar(33) not null,
 direction varchar(3) not null,					-- In or Out with respect to the proxy.
 created varchar(33) not null,				-- Time the cdr was created by the proxy.
 dst varchar(128),								-- The user portion of the destination URI.
 dsthost varchar(128) not null,					-- The host portion of the destination URI.
 dsturi varchar(1024) not null,					-- The full destination URI.
 fromuser varchar(128),							-- The user portion of the From header URI.
 fromname varchar(128),							-- The name portion of the From header.
 fromheader varchar(1024),						-- The full From header.
 callid varchar(256) not null,					-- The Call-ID of the call.
 localsocket varchar(64) not null,				-- The socket on the proxy used for the call.
 remotesocket varchar(64) not null,				-- The remote socket used for the call.
 bridgeid varchar(36),							-- If the call was involved in a bridge the id of it.
 inprogresstime varchar(33) null default null,-- The time of the last info response for the call.
 inprogressstatus int,							-- The SIP response status code of the last info response for the call.
 inprogressreason varchar(512),					-- The SIP response reason phrase of the last info response for the call.
 ringduration int,								-- Number of seconds the call was ringing for.
 answeredtime varchar(33) null default null,	-- The time the call was answered with a final response.
 answeredstatus int,							-- The SIP response status code of the final response for the call.
 answeredreason varchar(512),					-- The SIP response reason phrase of the final response for the call.
 duration int,									-- Number of seconds the call was established for.
 hunguptime varchar(33) null default null,	-- The time the call was hungup.
 hungupreason varchar(512),						-- The SIP response Reason header on the BYE request if present.
 Primary Key(id)
);

-- Telis Dial Plan Wizard Tables.

create table sipdialplanlookups
(
  id varchar(36) not null,
  owner varchar(32) not null,
  dialplanid varchar(36) not null,				-- The wizard dialplan the lookup entries will be used in.
  lookupkey varchar(128) not null,
  lookupvalue varchar(128) null,
  description varchar(256) null,
  lookuptype int not null,						-- 1=SpeedDial, 2=CNAM, 3=ENUM
  Primary Key(id),
  Foreign Key(dialplanid) references SIPDialPlans(id) on delete cascade on update cascade
);

create table sipdialplanproviders
(
  id varchar(36) not null,
  owner varchar(32) not null,
  dialplanid varchar(36) not null,				-- The wizard dialplan the provider entries will be used in.
  providername varchar(32) not null,
  providerprefix varchar(8) null,
  providerdialstring varchar(1024) not null,
  providerdescription varchar(256) null,
  Primary Key(id),
  Foreign Key(dialplanid) references SIPDialPlans(id) on delete cascade on update cascade
);

create table sipdialplanroutes
(
  id varchar(36) not null,
  owner varchar(32) not null,
  dialplanid varchar(36) not null,				-- The wizard dialplan the route entries will be used in.
  routename varchar(32) not null,
  routepattern varchar(256) not null,
  routedestination varchar(1024) not null,
  routedescription varchar(256) null,
  Primary Key(id),
  Foreign Key(dialplanid) references SIPDialPlans(id) on delete cascade on update cascade
);

create table sipdialplanoptions
(
  id varchar(36) not null,
  owner varchar(32) not null,
  dialplanid varchar(36) not null,				-- The wizard dialplan the options will be used in.
  timezone varchar(128) null,
  countrycode int null,
  areacode int null,
  allowedcountrycodes varchar(1024) null,
  excludedprefixes varchar(2048) null,
  enumservers varchar(2048) null,
  whitepageskey varchar(256) null,
  enablesafeguards bit default 0 not null,
  Primary Key(id),
  Foreign Key(dialplanid) references SIPDialPlans(id) on delete cascade on update cascade
);

-- Simple Dial Plan Wizard Tables.

create table SimpleWizardRule
(
  ID nvarchar(36) not null,
  Owner nvarchar(32) not null,
  DialPlanID nvarchar(36) not null,				-- The simple wizard dialplan the lookup entries will be used in.
  Direction nvarchar(3) not null,				-- In or Out dialplan rule.
  Priority decimal(5,3) not null,
  Description nvarchar(50) null,
  ToMatchType nvarchar(50) null,				-- Any, ToSIPAccount, ToSIPProvider, Regex.
  ToMatchParameter nvarchar(2048) null,
  ToSIPAccount nvarchar(161) null,				-- For incoming rules this can optionally hold the To SIP account the rule is for.
  ToProvider nvarchar(50) null,					-- For incoming rules this can optionally hold the To Provider the rule is for.
  PatternType nvarchar(16) null,
  Pattern nvarchar(1024) null,
  Command nvarchar(32) not null,				-- The dialplan command, e.g. Dial, Respond
  CommandParameter1 nvarchar(2048) not null,	
  CommandParameter2 nvarchar(2048),
  CommandParameter3 nvarchar(2048),
  TimePattern nvarchar(32) null,							-- If set refers to a time interval that dictates when this rule should apply
  IsDisabled bit not null default 0,						-- If set to 1 means the rule is disabled.
  Primary Key(ID),
  Foreign Key(DialPlanID) references SIPDialPlans(id) on delete cascade on update cascade
);

create table WebCallback
(
  ID nvarchar(36) not null,
  Owner nvarchar(32) not null,
  DialString1 nvarchar(256) not null,
  DialString2 nvarchar(256) not null,
  Description nvarchar(128) null,
  Inserted varchar(33) not null,
  Primary Key(ID),
  Foreign Key(owner) references Customers(customerusername) on delete cascade on update cascade
);

-- insert into sipdomains values ('5f971a0f-7876-4073-abe4-760a59bab940', 'sipsorcery.com', 'local;sipsorcery;sip.sipsorcery.com;sipsorcery.com:5060;sip.sipsorcery.com:5060;10.1.1.2;10.1.1.2:5060', null, '2010-02-09T13:01:21.3540000+00:00');
-- insert into sipdomains values ('9822C7A7-5358-42DD-8905-DC7ABAE3EC3A', 'demo.sipsorcery.com', 'local;demo.sipsorcery.com:5060;199.230.56.92;199.230.56.92:5060', null, '2010-10-15T00:00:00.0000000+00:00');
-- insert into sipdomains values ('9822C7A7-5358-42DD-8905-DC7ABAE3EC3A', 'sipsorcery.com', 'local;10.1.1.2;10.1.1.2:5060', null, '2010-10-15T00:00:00.0000000+00:00');
-- insert into customers (id, customerusername, customerpassword, emailaddress, adminid, maxexecutioncount, executioncount, emailaddressconfirmed, inserted, servicelevel) values ('AE246619-29ED-408C-A1C3-EA9E77C430A1', 'aaron', 'password', 'aaron@sipsorcery.com', '*', 5, 0, 1, '2010-10-15T00:00:00.0000000+00:00', 'Gold');

-- SIP Sorcery User Data DDL

create table dialplandata 
(
  dataowner varchar(32)not null,
  datakey varchar(64) not null, 
  datavalue varchar(1024) not null,
  Primary Key(dataowner, datakey)
);

create index cdrs_lastname_index on cdr(created);
create index cdrs_owner_index on cdr(owner);
create index providerbindings_nextregtime_index on sipproviderbindings(nextregistrationtime);
create index regbindings_contact_index on sipregistrarbindings(contacturi);
create index customers_custid_index on customers(customerusername);
create index regbindings_sipaccid_index on sipregistrarbindings(sipaccountid);

CREATE TABLE PayPalIPN
(
    ID varchar(36) not null,
    RawRequest nvarchar(10000) NOT NULL,
    ValidationResponse nvarchar(1024) NULL,
	IsSandbox bit not null,
    TransactionID nvarchar(256) NULL,
	TransactionType nvarchar(64) null,
    PayerFirstName nvarchar(256) NULL,
    PayerLastName nvarchar(256) NULL,
    PayerEmailAddress nvarchar(1024) NULL,
    Currency nvarchar(6)  NULL,
    Total decimal(6,3)  NULL,
    PayPalFee decimal(6,3)  NULL,
    Inserted datetime NOT NULL,
    ItemId nvarchar(256),
	CustomerID nvarchar(128) NULL,
	ActionTaken nvarchar(2048) NULL,
	Primary Key(ID)
);

create table VoxalotMigration
(
  ID varchar(36) not null,
  FullName nvarchar(128) not null,
  EmailAddress nvarchar(128) not null, 
  VoxalotAccountNumber nvarchar(10) not null,
  VoxalotAccountType nvarchar(32) not null,
  VoxalotExport nvarchar(8000) not null,
  Inserted nvarchar(33) not null,
  Primary Key(ID)
);

