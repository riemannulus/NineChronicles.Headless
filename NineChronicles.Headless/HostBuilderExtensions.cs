using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NineChronicles.Headless.Properties;
using System.Net;
using Lib9c.Formatters;
using Libplanet.Action;
using Libplanet.Crypto;
using Libplanet.Headless.Hosting;
using MessagePack;
using MessagePack.Resolvers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Nekoyume.Action;
using NineChronicles.Headless.Middleware;
using Sentry;

namespace NineChronicles.Headless
{
    public static class HostBuilderExtensions
    {
        public static IHostBuilder UseNineChroniclesNode(
            this IHostBuilder builder,
            NineChroniclesNodeServiceProperties properties,
            StandaloneContext context,
            ActionEvaluationPublisher actionEvaluationPublisher,
            NineChroniclesNodeService service
        )
        {
            return builder.ConfigureServices(services =>
            {
                services.AddOptions();
                services.AddHostedService(provider => service);
                services.AddSingleton(provider => service);
                services.AddSingleton(provider => service.Swarm);
                services.AddSingleton(provider => service.BlockChain);
                services.AddSingleton(provider => service.Store);

                if (properties.Libplanet is { } libplanetNodeServiceProperties)
                {
                    services.AddSingleton<LibplanetNodeServiceProperties>(provider => libplanetNodeServiceProperties);
                }

                services.AddSingleton(_ => actionEvaluationPublisher);
            });
        }

        public static IHostBuilder UseNineChroniclesRPC(
            this IHostBuilder builder,
            RpcNodeServiceProperties properties,
            ActionEvaluationPublisher actionEvaluationPublisher,
            StandaloneContext standaloneContext,
            IConfiguration configuration
        )
        {
            var context = new RpcContext
            {
                RpcRemoteSever = properties.RpcRemoteServer
            };

            return builder
                .ConfigureServices(services =>
                {
                    if (Convert.ToBoolean(configuration.GetSection("MultiAccountManaging")["EnableManaging"]))
                    {
                        services.Configure<MultiAccountManagerProperties>(configuration.GetSection("MultiAccountManaging"));
                    }

                    services.AddSingleton(_ => context);
                    services.AddGrpc(options =>
                    {
                        options.MaxReceiveMessageSize = null;
                        if (Convert.ToBoolean(configuration.GetSection("MultiAccountManaging")["EnableManaging"]))
                        {
                            Dictionary<string, HashSet<Address>> ipSignerList = new();
                            options.Interceptors.Add<GrpcMultiAccountManagementMiddleware>(
                                standaloneContext,
                                ipSignerList,
                                actionEvaluationPublisher);
                        }

                        options.Interceptors.Add<GrpcCaptureMiddleware>(actionEvaluationPublisher);
                    });
                    services.AddMagicOnion();
                    services.AddSingleton(_ => actionEvaluationPublisher);
                    var resolver = MessagePack.Resolvers.CompositeResolver.Create(
                        NineChroniclesResolver.Instance,
                        StandardResolver.Instance
                    );
                    var options = MessagePackSerializerOptions.Standard.WithResolver(resolver);
                    MessagePackSerializer.DefaultOptions = options;
                })
                .ConfigureWebHostDefaults(hostBuilder =>
                {
                    hostBuilder.ConfigureKestrel(options =>
                    {
                        options.ListenAnyIP(properties.RpcListenPort, listenOptions =>
                        {
                            listenOptions.Protocols = HttpProtocols.Http2;
                        });
                    });
                });
        }
    }
}
