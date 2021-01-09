==> Auto generate for MSSQL:
dotnet ef dbcontext scaffold --use-database-names -o DataAccess/AutoGen --context SIPAssetsDbContext --namespace demo.DataAccess -f "Data Source=localhost;Initial Catalog=SIPAssets;Persist Security Info=True;User ID=appuser;Password=password" Microsoft.EntityFrameworkCore.SqlServer

and once the connection string is stored in the user secrets store:

dotnet ef dbcontext scaffold --use-database-names -o DataAccess/AutoGen --context SIPAssetsDbContext --namespace demo.DataAccess -f Name=ConnectionStrings.SIPAssets Microsoft.EntityFrameworkCore.SqlServer

==> To generate SQL to initialise DB:
1. Set the desired optionsBuiler.Use* command from one of the above,
2. Ensure the project still builds,
3. On the command line in the same directory as the project file execute:
 dotnet ef migrations script 0 Initial

==> Store connection string in secret store
examples\SIPExamples\SIPAspNetServer> dotnet user-secrets init
examples\SIPExamples\SIPAspNetServer> dotnet user-secrets set ConnectionStrings.SIPAssets "Data Source=localhost;Initial Catalog=SIPAssets;Persist Security Info=True;User ID=appuser;Password=password"

==> Configuring different EF Core Providers:
https://docs.microsoft.com/en-us/ef/core/dbcontext-configuration/#configuring-the-database-provider

optionsBuilder.UseSqlServer("Data Source=localhost;Initial Catalog=SIPAssets;Persist Security Info=True;User ID=appuser;Password=password");
optionsBuilder.UseSqlServer("Name=ConnectionStrings:SIPAssets");
optionsBuilder.UseSqlite("Data Source=AppData/sipassets.db");

==> Auto generate for Sqlite:
dotnet ef dbcontext scaffold --use-database-names -o DataAccess/AutoGen --context SIPAssetsDbContext --namespace demo.DataAccess -f "Data Source=AppData/sipassets.db" Microsoft.EntityFrameworkCore.Sqlite

==> Reference for Using DbContextFactory (typically only DbContext is used with ASP.NET Core dependency injection)
https://docs.microsoft.com/en-us/ef/core/dbcontext-configuration/#using-a-dbcontext-factory-eg-for-blazor