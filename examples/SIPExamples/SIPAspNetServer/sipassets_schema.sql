begin transaction;

create table SIPDomains
(
 ID uniqueidentifier not null,
 Domain nvarchar(128) not null,			-- The domain name.
 AliasList nvarchar(1024),				-- If not null indicates a semi-colon delimited list of aliases for the domain.
 Inserted datetimeoffset not null default sysdatetimeoffset(),
 Primary Key(ID),
 Unique(domain)
);

create table SIPAccounts
(
 ID uniqueidentifier not null,
 DomainID uniqueidentifier not null,
 SIPUsername nvarchar(32) not null,
 SIPPassword nvarchar(32) not null,
 IsDisabled bit not null default 0,
 Inserted datetimeoffset not null default sysdatetimeoffset(),
 Primary Key(ID),
 Foreign Key(DomainID) references SIPDomains(ID) on delete cascade on update cascade,
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
 LastUpdate datetimeoffset not null,
 ExpiryTime datetimeoffset not null,
 Primary Key(ID),
 Foreign Key(SIPAccountID) references SIPAccounts(ID) on delete cascade on update cascade,
);

commit;
