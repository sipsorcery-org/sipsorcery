==> Auto generate for MSSQL:
dotnet ef dbcontext scaffold --use-database-names -o DataAccess/AutoGen --context SIPAssetsDbContext --namespace demo.DataAccess -f "Data Source=localhost;Initial Catalog=SIPAssets;Persist Security Info=True;User ID=appuser;Password=password" Microsoft.EntityFrameworkCore.SqlServer

==> Auto generate for Sqlite:
dotnet ef dbcontext scaffold --use-database-names -o DataAccess/AutoGen --context SIPAssetsDbContext --namespace demo.DataAccess -f "Data Source=AppData/sipassets.db" Microsoft.EntityFrameworkCore.Sqlite

optionsBuilder.UseSqlServer("Data Source=localhost;Initial Catalog=SIPAssets;Persist Security Info=True;User ID=appuser;Password=password");
optionsBuilder.UseSqlite("Data Source=AppData/sipassets.db");

==> To generate SQL to initialise DB:
1. Set the desired optionsBuiler.Use* command from one of the above,
2. Ensure the project still builds,
3. On the command line in the same directory as the project file execute:
 dotnet ef migrations script 0 Initial


