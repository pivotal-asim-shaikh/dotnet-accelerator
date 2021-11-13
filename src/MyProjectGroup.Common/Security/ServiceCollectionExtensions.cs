﻿using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using idunno.Authentication.Basic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Steeltoe.Management;
using Steeltoe.Management.Endpoint;
using Steeltoe.Management.Endpoint.SpringBootAdminClient;

namespace MyProjectGroup.Common.Security
{
    public static class ServiceCollectionExtensions
    {
        private const string SpringBootAdminHostedServiceType =
            "Steeltoe.Management.Endpoint.SpringBootAdminClient.SpringBootAdminClientHostedService, Steeltoe.Management.EndpointBase";
        public static IServiceCollection AddSpringBootAdmin(this IServiceCollection services)
        {
            services.AddSafeSpringBootAdmin();
            services.AddSingleton(provider =>
            {
                var actuatorSecurityOptions = provider.GetRequiredService<IOptions<ActuatorSecurityOptions>>().Value;
                var springBootAdminOptions = ActivatorUtilities.CreateInstance<SpringBootAdminClientOptions>(provider);
                springBootAdminOptions.Metadata ??= new();
                springBootAdminOptions.Metadata.TryAdd("user.name", actuatorSecurityOptions.UserName);
                springBootAdminOptions.Metadata.TryAdd("user.password", actuatorSecurityOptions.Password);
                return springBootAdminOptions;
            });
            return services;
        }

        /// <summary>
        /// Addresses bug in Steeltoe when app crashes if it can't register with Spring Boot Admin. This changes it to a warning 
        /// </summary>
        public static IServiceCollection AddSafeSpringBootAdmin(this IServiceCollection services)
        {
            services.AddSpringBootAdminClient();
            var service = services.Last(x => x.ServiceType == typeof(IHostedService));
            services.Remove(service);
            services.AddHostedService<SpringBootAdminSafeStartupHostedService>();
            return services;
        }

        private class SpringBootAdminSafeStartupHostedService : IHostedService
        {
            private readonly SpringBootAdminClientOptions _options;
            private readonly ILogger<SpringBootAdminSafeStartupHostedService> _logger;
            private readonly IHostedService? _springBootAdminHostedService;
            public SpringBootAdminSafeStartupHostedService(IServiceProvider serviceProvider, SpringBootAdminClientOptions options, ILogger<SpringBootAdminSafeStartupHostedService> logger)
            {
                _options = options;
                _logger = logger;
                var type = Type.GetType(SpringBootAdminHostedServiceType);
                if (type == null)
                    return;
                _springBootAdminHostedService = (IHostedService)ActivatorUtilities.CreateInstance(serviceProvider, type);
            }

            public Task StartAsync(CancellationToken cancellationToken)
            {
                if (_springBootAdminHostedService != null)
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            await _springBootAdminHostedService.StartAsync(cancellationToken);
                            _logger.LogInformation("Successfully registered with Spring Boot Admin at {Url}", _options.Url);
                        }
                        catch (Exception)
                        {
                            _logger.LogWarning("Can't connect to Spring Boot Admin at {Url}", _options.Url);
                        }
                    }, cancellationToken);
                }
                return Task.CompletedTask;
            }

            public async Task StopAsync(CancellationToken cancellationToken)
            {
                if (_springBootAdminHostedService == null)
                    return;
                try
                {
                    await _springBootAdminHostedService.StopAsync(cancellationToken);
                }
                catch (Exception e)
                {
                    _logger.LogWarning(e, $"Can't connect to Spring Boot Admin at {_options.Url}");
                }
            }
        }
        public static IServiceCollection AddSecureActuators(this IServiceCollection services)
        {
            services.AddAllActuators();
            services.AddSingleton<IStartupFilter>(new AllActuatorsStartupFilter(c => c.RequireAuthorization(KnownAuthorizationPolicy.Actuators)));
            services.AddOptions<ActuatorSecurityOptions>()
                .Configure<IConfiguration>((options, config) =>
                {
                    var section = config.GetSection("Spring:Boot:Admin:Client:Metadata");
                    var username = section.GetValue<string>("user.name");
                    var password = section.GetValue<string>("user.password");
                    if (username != null)
                    {
                        options.UserName = username;
                    }

                    if (password != null)
                    {
                        options.Password = password;
                    }

                    options.Enabled = config.GetValue<bool?>("Management:Endpoints:Actuator:EnableSecurity") ?? true;
                });
            services.AddAuthentication().AddBasic(BasicAuthenticationDefaults.AuthenticationScheme, options =>
            {
                options.AllowInsecureProtocol = true;
                options.Events = new BasicAuthenticationEvents
                {
                    OnValidateCredentials = context =>
                    {
                        var options = context.HttpContext.RequestServices.GetRequiredService<IOptionsSnapshot<ActuatorSecurityOptions>>().Value;
                        if (context.Username == options.UserName && context.Password == options.Password) 
                        {
                            context.Principal = new ClaimsPrincipal(new ClaimsIdentity(new[] {new Claim("scope", KnownScope.Actuators)}));
                            context.Success();
                        }

                        return Task.CompletedTask;
                    }
                };
            });
            
            services.AddAuthorization(authz =>
            {
                authz.AddPolicy(KnownAuthorizationPolicy.Actuators, policyBuilder => policyBuilder
                    .AddAuthenticationSchemes(BasicAuthenticationDefaults.AuthenticationScheme)
                    .RequireAssertion(context =>
                    {
                        var httpContext = (HttpContext) context.Resource!;
                        var options = httpContext.RequestServices.GetRequiredService<IOptionsSnapshot<ActuatorSecurityOptions>>().Value;
                        if (!options.Enabled)
                        {
                            return true;
                        }
                        var actuatorEndpoints = httpContext.RequestServices.GetServices<IEndpointOptions>()
                            .Where(x => x.Id is not "health" and not "info")
                            .Select(x => ((PathString) $"/actuator").Add($"/{x.Id}"))
                            .ToList();
                        var path = httpContext.Request.Path;
                        if (actuatorEndpoints.Any(x => path.StartsWithSegments(x)) && !context.User.HasScope(KnownScope.Actuators))
                        {
                            return false;
                        }
                        
                        return true;
                    })
                );
            });
            return services;
        }
    }
}