using System.Reflection;
using IplFranchise.DbMaintenance.Configuration;
using IplFranchise.DbMaintenance.Schema;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IplFranchise.DbMaintenance.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDbMaintenance(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DbMaintenanceOptions>()
            .Bind(configuration.GetSection(DbMaintenanceOptions.SectionName))
            .ValidateDataAnnotations();

        // One CosmosClient for the process (it is thread-safe and holds the session token).
        services.AddSingleton(sp =>
            CosmosClientFactory.Create(sp.GetRequiredService<IOptions<DbMaintenanceOptions>>().Value.ConnectionString));

        // Every concrete Migration in this assembly is picked up, so adding a migration is just adding a class.
        var migrationTypes = Assembly.GetExecutingAssembly().GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.IsSubclassOf(typeof(Migration)));

        foreach (var type in migrationTypes)
        {
            services.AddTransient(typeof(Migration), type);
        }

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<SchemaVerifier>();
        services.AddSingleton<LocalProvisioner>();
        services.AddSingleton<MigrationRunner>();
        return services;
    }
}
