insert into sipdomains values ('899e48ef-1267-4b53-8a1c-476a176a4e80', 'democloud.sipsorcery.com', '*;192.168.0.50', CURRENT_TIMESTAMP);
insert into sipaccounts values ('D8E27F4E-CB6F-4B3A-A50F-9AA90350F141', '899e48ef-1267-4b53-8a1c-476a176a4e80', null, 'aaron', 'password', '0', CURRENT_TIMESTAMP);
insert into sipaccounts values ('57F94071-43B6-4C58-A6FE-5018DC755B81', '899e48ef-1267-4b53-8a1c-476a176a4e80', null, 'user', 'password', '0', CURRENT_TIMESTAMP);
insert into sipdialplans values (newid(), 'default', 'var inUri = uasTx.TransactionRequestURI;

switch (inUri.User)
{
    case "123":
        //return new SIPCallDescriptor("time@sipsorcery.com", uasTx.TransactionRequest.Body);
        return new SIPCallDescriptor("aaron@192.168.0.50:6060", uasTx.TransactionRequest.Body);
    case "456":
        return new SIPCallDescriptor("idontexist@sipsorcery.com", uasTx.TransactionRequest.Body);
    default:
        return null;
}',
CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, '0');