==> Sends an OPTIONS request.
dotnet run -- -d sip:192.168.0.50

==> Initiates a call and hangups if/when answered. Good for checking CDRs are generated correctly.
dotnet run -- -d sip:123@192.168.0.50 -s uac

==> Sends a registration request. Uses user/password for credentials.
dotnet run -- -d sip:192.168.0.50 -s reg
insert into sipaccounts values (newid(), '899e48ef-1267-4b53-8a1c-476a176a4e80', 'user', 'password', '0', default);

==> Web API URLs
https://localhost:5001/swagger/index.html
https://localhost:5001/api/sipaccounts
https://localhost:5001/api/sipdomains
https://localhost:5001/api/sipregistrarbindings
https://localhost:5001/api/sipcalls
https://localhost:5001/api/cdrs