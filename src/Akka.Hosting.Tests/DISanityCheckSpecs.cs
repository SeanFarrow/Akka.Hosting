﻿using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Akka.Hosting.Tests;

public class DiSanityCheckSpecs
{
    public interface IMySingletonInterface{}
    
    public sealed class MySingletonImpl : IMySingletonInterface{}

    public sealed class SingletonActor : ReceiveActor
    {
        public sealed class GetSingleton
        {
            public static readonly GetSingleton Instance = new GetSingleton();
            private GetSingleton(){}
        }
        
        private readonly IMySingletonInterface _singleton;

        public SingletonActor(IMySingletonInterface singleton)
        {
            _singleton = singleton;

            Receive<GetSingleton>(_ =>
            {
                Sender.Tell(_singleton);
            });
        }
    }
    
    private static async Task<IHost> StartHost(Action<IServiceCollection> testSetup)
    {
        var host = new HostBuilder()
        .ConfigureServices(services =>
        {
            services.AddSingleton<IMySingletonInterface, MySingletonImpl>();
            testSetup(services);
        }).Build();
        
        await host.StartAsync();
        return host;
    }

    /// <summary>
    /// Sanity check: things registered as singletons prior to the creation of the <see cref="ActorSystem"/> should
    /// still be singletons when working with Akka.DependencyInjection.
    /// </summary>
    [Fact]
    public async Task ShouldNotRecreateContainerMembersUsingActorDi()
    {
        // arrange
        using var host = await StartHost(collection =>
        {
            collection.AddAkka("MyActorSys", builder =>
            {
                builder.WithActors((system, registry) =>
                {
                    var props = DependencyResolver.For(system).Props<SingletonActor>();
                    var singletonActor = system.ActorOf(props, "singleton");
                    registry.TryRegister<SingletonActor>(singletonActor);
                });
            });
        });
        
        // act
        var singletonInstance = host.Services.GetRequiredService<IMySingletonInterface>();
        var singletonActor = host.Services.GetRequiredService<ActorRegistry>().Get<SingletonActor>();
        var singletonFromActor =
            await singletonActor.Ask<IMySingletonInterface>(SingletonActor.GetSingleton.Instance, TimeSpan.FromSeconds(3));

        // assert
        singletonFromActor.Should().Be(singletonInstance);
    }
    
    /// <summary>
    /// Sanity check: things registered as singletons prior to the creation of the <see cref="ActorSystem"/> should
    /// still be singletons when working explicitly with the <see cref="IServiceProvider"/> during initialization of actors.
    /// </summary>
    [Fact]
    public async Task ShouldNotRecreateContainerMembersUsingServiceProviderDuringStart()
    {
        // arrange
        using var host = await StartHost(collection =>
        {
            collection.AddAkka("MyActorSys", (builder, sp) =>
            {
                builder.WithActors((system, registry) =>
                {
                    var singleton = sp.GetRequiredService<IMySingletonInterface>();
                    var singletonActor = system.ActorOf(Props.Create(() => new SingletonActor(singleton)), "singleton");
                    registry.TryRegister<SingletonActor>(singletonActor);
                });
            });
        });
        
        // act
        var singletonInstance = host.Services.GetRequiredService<IMySingletonInterface>();
        var singletonActor = host.Services.GetRequiredService<ActorRegistry>().Get<SingletonActor>();
        var singletonFromActor =
            await singletonActor.Ask<IMySingletonInterface>(SingletonActor.GetSingleton.Instance, TimeSpan.FromSeconds(3));

        // assert
        singletonFromActor.Should().Be(singletonInstance);
    }
}