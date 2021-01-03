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