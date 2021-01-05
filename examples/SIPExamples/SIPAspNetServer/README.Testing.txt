==> Sends an OPTIONS request.
examples\sipcmdline> dotnet run -- -d sip:192.168.0.50

==> Initiates a call and hangups if/when answered. Good for checking CDRs are generated correctly.
examples\sipcmdline> dotnet run -- -d sip:123@192.168.0.50 -s uac
examples\sipcmdline> dotnet run -- -d sip:456@192.168.0.50 -s uac

==> Sends a registration request. Uses user/password for credentials.
examples\sipcmdline> dotnet run -- -d sip:192.168.0.50 -s reg

==> Web API URLs
https://localhost:5001/swagger/index.html
https://localhost:5001/api/sipaccounts
https://localhost:5001/api/sipdomains
https://localhost:5001/api/sipregistrarbindings
https://localhost:5001/api/sipcalls
https://localhost:5001/api/cdrs

==> Test Web API:
curl -X POST "https://localhost:5001/api/SIPAccounts" -H "accept: text/plain" -H  "Content-Type: application/json" -d "{\"id\":\"3fa85f64-5717-4562-b3fc-2c963f66afa6\",\"sipUsername\":\"aaron\",\"sipPassword\":\"password\",\"owner\":\"\",\"Sipdomain\":\"aspnet.sipsorcery.com\",\"isDisabled\":false,\"inserted\":\"2020-12-29T00:00:00.0000000+00:00\"}"