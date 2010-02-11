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
 ipaddress varchar(15),
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
 providerserver varchar(256) not null,
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
 Foreign Key(owner) references customers(customerusername),
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
 dialplanscript varchar(30000),
 scripttypedescription varchar(12) not null default 'Ruby',		-- The type of script the dialplan has, supported values are: Asterisk, Ruby, Python and JScript.
 inserted varchar(33) not null,
 lastupdate varchar(33) not null,
 maxexecutioncount int not null,								-- The mamimum number of simultaneous executions of the dialplan that are permitted.
 executioncount int not null,									-- The current number of dialplan executions in progress.
 authorisedapps varchar(2048),									-- A semi-colon delimited list of privileged apps that this dialplan is authorised to use.
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
 sdp varchar(2048),
 remotesdp varchar(2048),
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
 inprogressreason varchar(64),					-- The SIP response reason phrase of the last info response for the call.
 ringduration int,								-- Number of seconds the call was ringing for.
 answeredtime varchar(33) null default null,	-- The time the call was answered with a final response.
 answeredstatus int,							-- The SIP response status code of the final response for the call.
 answeredreason varchar(64),					-- The SIP response reason phrase of the final response for the call.
 duration int,									-- Number of seconds the call was established for.
 hunguptime varchar(33) null default null,	-- The time the call was hungup.
 hungupreason varchar(64),						-- The SIP response Reason header on the BYE request if present.
 Primary Key(id)
);

create index customers_custid_index on customers(customerusername);
create index cdrs_lastname_index on cdr(created);
create index cdrs_owner_index on cdr(owner);
create index providerbindings_nextregtime_index on sipproviderbindings(nextregistrationtime);
create index regbindings_sipaccid_index on sipregistrarbindings(sipaccountid);
create index regbindings_contact_index on sipregistrarbindings(contacturi);

--insert into sipdomains values ('5f971a0f-7876-4073-abe4-760a59bab940', 'sipsorcery.com', 'local;sipsorcery;sip.sipsorcery.com;sipsorcery.com:5060;sip.sipsorcery.com:5060;174.129.236.7;174.129.236.7:5060', null, ‘2010-02-09T13:01:21.3540000+00:00’);

-- SIP Sorcery User Data DDL

create table dialplandata 
(
  dataowner varchar(32)not null,
  datakey varchar(64) not null, 
  datavalue varchar(1024) not null,
  Primary Key(dataowner, datakey)
);
