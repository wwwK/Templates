namespace GraphQLTemplate
{
    using System;
#if ResponseCompression
    using System.IO.Compression;
#endif
    using System.Linq;
    using Boxed.AspNetCore;
    using GraphQL;
#if Authorization
    using GraphQL.Authorization;
#endif
    using GraphQL.Server;
    using GraphQL.Server.Internal;
    using GraphQL.Validation;
#if (Authorization || CORS)
    using GraphQLTemplate.Constants;
#endif
    using GraphQLTemplate.Options;
    using Microsoft.AspNetCore.Builder;
#if (!ForwardedHeaders && HostFiltering)
    using Microsoft.AspNetCore.HostFiltering;
#endif
    using Microsoft.AspNetCore.Hosting;
#if OpenTelemetry
    using Microsoft.AspNetCore.Http;
#endif
#if ResponseCompression
    using Microsoft.AspNetCore.ResponseCompression;
#endif
    using Microsoft.AspNetCore.Server.Kestrel.Core;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Options;
#if OpenTelemetry
    using OpenTelemetry;
    using OpenTelemetry.Exporter;
    using OpenTelemetry.Resources;
    using OpenTelemetry.Trace;
#endif

    /// <summary>
    /// <see cref="IServiceCollection"/> extension methods which extend ASP.NET Core services.
    /// </summary>
    internal static class CustomServiceCollectionExtensions
    {
        /// <summary>
        /// Configures caching for the application. Registers the <see cref="IDistributedCache"/> and
        /// <see cref="IMemoryCache"/> types with the services collection or IoC container. The
        /// <see cref="IDistributedCache"/> is intended to be used in cloud hosted scenarios where there is a shared
        /// cache, which is shared between multiple instances of the application. Use the <see cref="IMemoryCache"/>
        /// otherwise.
        /// </summary>
        /// <param name="services">The services.</param>
        /// <returns>The services with caching services added.</returns>
        public static IServiceCollection AddCustomCaching(this IServiceCollection services) =>
            services
                .AddMemoryCache()
                // Adds IDistributedCache which is a distributed cache shared between multiple servers. This adds a
                // default implementation of IDistributedCache which is not distributed. You probably want to use the
                // Redis cache provider by calling AddDistributedRedisCache.
                .AddDistributedMemoryCache();
#if CORS

        /// <summary>
        /// Add cross-origin resource sharing (CORS) services and configures named CORS policies (See
        /// https://docs.asp.net/en/latest/security/cors.html).
        /// </summary>
        /// <param name="services">The services.</param>
        /// <returns>The services with caching services added.</returns>
        public static IServiceCollection AddCustomCors(this IServiceCollection services) =>
            services.AddCors(
                options =>
                    // Create named CORS policies here which you can consume using application.UseCors("PolicyName")
                    // or a [EnableCors("PolicyName")] attribute on your controller or action.
                    options.AddPolicy(
                        CorsPolicyName.AllowAny,
                        x => x
                            .AllowAnyOrigin()
                            .AllowAnyMethod()
                            .AllowAnyHeader()));
#endif

        /// <summary>
        /// Configures the settings by binding the contents of the appsettings.json file to the specified Plain Old CLR
        /// Objects (POCO) and adding <see cref="IOptions{T}"/> objects to the services collection.
        /// </summary>
        /// <param name="services">The services.</param>
        /// <param name="configuration">The configuration.</param>
        /// <returns>The services with caching services added.</returns>
        public static IServiceCollection AddCustomOptions(
            this IServiceCollection services,
            IConfiguration configuration) =>
            services
                // ConfigureAndValidateSingleton registers IOptions<T> and also T as a singleton to the services collection.
                .ConfigureAndValidateSingleton<ApplicationOptions>(configuration)
                .ConfigureAndValidateSingleton<CacheProfileOptions>(configuration.GetSection(nameof(ApplicationOptions.CacheProfiles)))
#if ResponseCompression
                .ConfigureAndValidateSingleton<CompressionOptions>(configuration.GetSection(nameof(ApplicationOptions.Compression)))
#endif
#if ForwardedHeaders
                .ConfigureAndValidateSingleton<ForwardedHeadersOptions>(configuration.GetSection(nameof(ApplicationOptions.ForwardedHeaders)))
                .Configure<ForwardedHeadersOptions>(
                    options =>
                    {
                        options.KnownNetworks.Clear();
                        options.KnownProxies.Clear();
                    })
#elif HostFiltering
                .ConfigureAndValidateSingleton<HostFilteringOptions>(configuration.GetSection(nameof(ApplicationOptions.HostFiltering)))
#endif
                .ConfigureAndValidateSingleton<GraphQLOptions>(configuration.GetSection(nameof(ApplicationOptions.GraphQL)))
                .ConfigureAndValidateSingleton<KestrelServerOptions>(configuration.GetSection(nameof(ApplicationOptions.Kestrel)));
#if ResponseCompression

        /// <summary>
        /// Adds dynamic response compression to enable GZIP compression of responses. This is turned off for HTTPS
        /// requests by default to avoid the BREACH security vulnerability.
        /// </summary>
        /// <param name="services">The services.</param>
        /// <param name="configuration">The configuration.</param>
        /// <returns>The services with caching services added.</returns>
        public static IServiceCollection AddCustomResponseCompression(
            this IServiceCollection services,
            IConfiguration configuration) =>
            services
                .Configure<BrotliCompressionProviderOptions>(options => options.Level = CompressionLevel.Optimal)
                .Configure<GzipCompressionProviderOptions>(options => options.Level = CompressionLevel.Optimal)
                .AddResponseCompression(
                    options =>
                    {
                        // Add additional MIME types (other than the built in defaults) to enable GZIP compression for.
                        var customMimeTypes = configuration
                            .GetSection(nameof(ApplicationOptions.Compression))
                            .Get<CompressionOptions>()
                            ?.MimeTypes ?? Enumerable.Empty<string>();
                        options.MimeTypes = customMimeTypes.Concat(ResponseCompressionDefaults.MimeTypes);

                        options.Providers.Add<BrotliCompressionProvider>();
                        options.Providers.Add<GzipCompressionProvider>();
                    });
#endif

        /// <summary>
        /// Add custom routing settings which determines how URL's are generated.
        /// </summary>
        /// <param name="services">The services.</param>
        /// <returns>The services with caching services added.</returns>
        public static IServiceCollection AddCustomRouting(this IServiceCollection services) =>
            services.AddRouting(options => options.LowercaseUrls = true);
#if HttpsEverywhere

        /// <summary>
        /// Adds the Strict-Transport-Security HTTP header to responses. This HTTP header is only relevant if you are
        /// using TLS. It ensures that content is loaded over HTTPS and refuses to connect in case of certificate
        /// errors and warnings.
        /// See https://developer.mozilla.org/en-US/docs/Web/Security/HTTP_strict_transport_security and
        /// http://www.troyhunt.com/2015/06/understanding-http-strict-transport.html
        /// Note: Including subdomains and a minimum maxage of 18 weeks is required for preloading.
        /// Note: You can refer to the following article to clear the HSTS cache in your browser
        /// (See http://classically.me/blogs/how-clear-hsts-settings-major-browsers).
        /// </summary>
        /// <param name="services">The services.</param>
        /// <returns>The services with caching services added.</returns>
        public static IServiceCollection AddCustomStrictTransportSecurity(this IServiceCollection services) =>
            services
                .AddHsts(
                    options =>
                    {
                        // Preload the HSTS HTTP header for better security. See https://hstspreload.org/
#if HstsPreload
                        options.IncludeSubDomains = true;
                        options.MaxAge = TimeSpan.FromSeconds(31536000); // 1 Year
                        options.Preload = true;
#else
                        // options.IncludeSubDomains = true;
                        // options.MaxAge = TimeSpan.FromSeconds(31536000); // 1 Year
                        // options.Preload = true;
#endif
                    });
#endif
#if HealthCheck

        public static IServiceCollection AddCustomHealthChecks(this IServiceCollection services) =>
            services
                .AddHealthChecks()
                // Add health checks for external dependencies here. See https://github.com/Xabaril/AspNetCore.Diagnostics.HealthChecks
                .Services;
#endif
#if OpenTelemetry

        /// <summary>
        /// Adds Open Telemetry services and configures instrumentation and exporters.
        /// </summary>
        /// <param name="services">The services.</param>
        /// <param name="webHostEnvironment">The environment the application is running under.</param>
        /// <returns>The services with open telemetry added.</returns>
        public static IServiceCollection AddCustomOpenTelemetryTracing(this IServiceCollection services, IWebHostEnvironment webHostEnvironment) =>
            services.AddOpenTelemetryTracing(
                builder =>
                {
                    builder
                        .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(webHostEnvironment.ApplicationName))
                        .AddAspNetCoreInstrumentation(
                            options =>
                            {
                                // Enrich spans with additional request and response meta data.
                                // See https://github.com/open-telemetry/opentelemetry-specification/blob/master/specification/trace/semantic_conventions/http.md
                                options.Enrich = (activity, eventName, obj) =>
                                {
                                    if (!activity.IsAllDataRequested)
                                    {
                                        return;
                                    }

                                    if (obj is HttpRequest request)
                                    {
                                        var context = request.HttpContext;
                                        activity.AddTag(OpenTelemetryAttributeName.Http.Flavor, GetHttpFlavour(request.Protocol));
                                        activity.AddTag(OpenTelemetryAttributeName.Http.Scheme, request.Scheme);
                                        activity.AddTag(OpenTelemetryAttributeName.Http.ClientIP, context.Connection.RemoteIpAddress);
                                        activity.AddTag(OpenTelemetryAttributeName.Http.RequestContentLength, request.ContentLength);
                                        activity.AddTag(OpenTelemetryAttributeName.Http.RequestContentType, request.ContentType);

                                        var user = context.User;
                                        if (user.Identity.Name is not null)
                                        {
                                            activity.AddTag(OpenTelemetryAttributeName.EndUser.Id, user.Identity.Name);
                                            activity.AddTag(OpenTelemetryAttributeName.EndUser.Scope, string.Join(',', user.Claims.Select(x => x.Value)));
                                        }
                                    }
                                    else if (obj is HttpResponse response)
                                    {
                                        activity.AddTag(OpenTelemetryAttributeName.Http.ResponseContentLength, response.ContentLength);
                                        activity.AddTag(OpenTelemetryAttributeName.Http.ResponseContentType, response.ContentType);
                                    }

                                    static string GetHttpFlavour(string protocol)
                                    {
                                        if (HttpProtocol.IsHttp10(protocol))
                                        {
                                            return "1.0";
                                        }
                                        else if (HttpProtocol.IsHttp11(protocol))
                                        {
                                            return "1.1";
                                        }
                                        else if (HttpProtocol.IsHttp2(protocol))
                                        {
                                            return "2.0";
                                        }
                                        else if (HttpProtocol.IsHttp3(protocol))
                                        {
                                            return "3.0";
                                        }

                                        throw new InvalidOperationException($"Protocol {protocol} not recognised.");
                                    }
                                };
                                options.RecordException = true;
                            });

                    if (webHostEnvironment.IsDevelopment())
                    {
                        builder.AddConsoleExporter(options => options.Targets = ConsoleExporterOutputTargets.Debug);
                    }

                    // TODO: Add OpenTelemetry.Instrumentation.* NuGet packages and configure them to collect more span data.
                    //       E.g. Add the OpenTelemetry.Instrumentation.Http package to instrument calls to HttpClient.
                    // TODO: Add OpenTelemetry.Exporter.* NuGet packages and configure them here to export open telemetry span data.
                    //       E.g. Add the OpenTelemetry.Exporter.OpenTelemetryProtocol package to export span data to Jaeger.
                });
#endif

        public static IServiceCollection AddCustomGraphQL(
            this IServiceCollection services,
            IConfiguration configuration,
            IWebHostEnvironment webHostEnvironment) =>
            services
                // Add a way for GraphQL.NET to resolve types.
                .AddSingleton<IDependencyResolver, GraphQLDependencyResolver>()
                .AddGraphQL(
                    options =>
                    {
                        var graphQLOptions = configuration
                            .GetSection(nameof(ApplicationOptions.GraphQL))
                            .Get<GraphQLOptions>();
                        // Set some limits for security, read from configuration.
                        options.ComplexityConfiguration = graphQLOptions.ComplexityConfiguration;
                        // Enable GraphQL metrics to be output in the response, read from configuration.
                        options.EnableMetrics = graphQLOptions.EnableMetrics;
                        // Show stack traces in exceptions. Don't turn this on in production.
                        options.ExposeExceptions = webHostEnvironment.IsDevelopment();
                    })
                // Adds all graph types in the current assembly with a singleton lifetime.
                .AddGraphTypes()
                // Adds ConnectionType<T>, EdgeType<T> and PageInfoType.
                .AddRelayGraphTypes()
                // Add a user context from the HttpContext and make it available in field resolvers.
                .AddUserContextBuilder<GraphQLUserContextBuilder>()
                // Add GraphQL data loader to reduce the number of calls to our repository.
                .AddDataLoader()
#if Subscriptions
                // Add WebSockets support for subscriptions.
                .AddWebSockets()
#endif
                .Services
                .AddTransient(typeof(IGraphQLExecuter<>), typeof(InstrumentingGraphQLExecutor<>));
#if Authorization

        /// <summary>
        /// Add GraphQL authorization (See https://github.com/graphql-dotnet/authorization).
        /// </summary>
        /// <param name="services">The services.</param>
        /// <returns>The services with caching services added.</returns>
        public static IServiceCollection AddCustomGraphQLAuthorization(this IServiceCollection services) =>
            services
                .AddSingleton<IAuthorizationEvaluator, AuthorizationEvaluator>()
                .AddTransient<IValidationRule, AuthorizationValidationRule>()
                .AddSingleton(
                    x =>
                    {
                        var authorizationSettings = new AuthorizationSettings();
                        authorizationSettings.AddPolicy(
                            AuthorizationPolicyName.Admin,
                            y => y.RequireClaim("role", "admin"));
                        return authorizationSettings;
                    });
#endif
    }
}
