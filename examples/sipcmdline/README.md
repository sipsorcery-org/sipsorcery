## Usage

This program is intended to allow testing of simple SIP scenarios from a command line.

You will need `.NET` installed.

To see all the options available use:

`dotnet run -- --help`

## Examples:

Description: Test a single SIP call acting as a User Agent Client (UAC).
 
`dotnet run -- -d music@iptel.org -s uac -v`

Description: Test a single authenticated SIP call acting as a User Agent Client (UAC).

`dotnet run -- -d 100@sipprovider.cloud -s uac -v -u user --password password`
`dotnet run -- -d 9170@192.168.0.47 -s uac -v -u 1000 --password password`
`dotnet run -- -d 100@192.168.0.48 -s uac -v -u 7004 --password password`
