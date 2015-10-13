using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Akka.Actor;
using Akka.Cluster;
using Akka.Configuration;
using Akka.Dispatch.SysMsg;

namespace AkkaTest
{
    class Program
    {
        static void Main(string[] args)
        {
            var config = @"
                akka {
                    stdout-loglevel = DEBUG
                    loglevel = DEBUG
                    log-config-on-start = off

                    actor {
                        provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
                    }

                    remote {
                        log-remote-lifecycle-events = DEBUG
                        helios.tcp {
                            hostname = ""127.0.0.1""
                            port = " + args[0] + @"
                        }
                    }
                }";

            var actorSystem = ActorSystem.Create("akkatest", ConfigurationFactory.ParseString(config));
            var pingActor = actorSystem.ActorOf<PingActor>("ping");


            while (true)
            {
                var command = Console.ReadLine();
                var commandArgs = command.Split(' ');
                switch (commandArgs[0].ToLowerInvariant())
                {
                    case "status":
                        Status(actorSystem);
                        break;
                    case "ping":
                        Ping(actorSystem, commandArgs);
                        break;
                    case "watch":
                        Watch(actorSystem, commandArgs);
                        break;
                    case "reliable-watch":
                        ReliableWatch(actorSystem, commandArgs);
                        break;
                    case "unwatch":
                        Unwatch(actorSystem, commandArgs);
                        break;
                    default:
                        Console.WriteLine($"Don't know command \"{command}\"");
                        break;

                }
            }
        }

        private static void ReliableWatch(ActorSystem actorSystem, string[] commandArgs)
        {
            var port = 0;
            if (!int.TryParse(commandArgs[1], out port))
            {
                Console.WriteLine("Invalid port: " + commandArgs[1]);
                return;
            }
            actorSystem.ActorOf(Props.Create(() => new ReliableWatcher(port)), "watcher" + port);
        }

        private static void Unwatch(ActorSystem actorSystem, string[] commandArgs)
        {
            var resolveOne = actorSystem.ActorSelection("user/watcher" + commandArgs[1]).ResolveOne(TimeSpan.FromSeconds(1));

            resolveOne.ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    Console.WriteLine("Could not find watcher" + commandArgs[1]);
                    return;
                }
                resolveOne.Result.Tell(PoisonPill.Instance);
            });
        }

        private static void Watch(ActorSystem actorSystem, string[] commandArgs)
        {
            var port = 0;
            if (!int.TryParse(commandArgs[1], out port))
            {
                Console.WriteLine("Invalid port: " + commandArgs[1]);
                return;
            }
            actorSystem.ActorOf(Props.Create(() => new Watcher(port)), "watcher" + port);
        }

        private static void Status(ActorSystem actorSystem)
        {
            //Console.WriteLine("Self: " + cluster.SelfUniqueAddress);
            //Console.WriteLine("ClusterStatus: " + GetStatus(cluster));
            //Console.WriteLine("ClusterLeader: " + cluster.ReadView.Leader);
            //Console.WriteLine("Members:");
            //Console.WriteLine("   " +
            //                  string.Join("\n   ", cluster.ReadView.Members.Select(x => $"{x.UniqueAddress} - {x.Status}")));
            //Console.WriteLine("UnreachableMembers: ");
            //Console.WriteLine("   " +
            //                  string.Join("\n   ",
            //                      cluster.ReadView.Reachability.AllUnreachable.Select(x => $"{x.Address} {x.Uid}")));
        }

        private static void Ping(ActorSystem actorSystem, string[] commandArgs)
        {
            actorSystem.ActorSelection($"akka.tcp://akkatest@127.0.0.1:{commandArgs[1]}/user/ping")
                .Ask<string>("ping")
                .ContinueWith(x =>
                {
                    if (x.IsFaulted)
                    {
                        Console.WriteLine("Got faulty ping response: " + x.Exception?.ToString());
                    }
                    else
                    {
                        Console.WriteLine("Got ping response: " + x.Result);
                    }
                });
        }
    }

    public class Watcher : ReceiveActor
    {
        private readonly int _port;

        public Watcher(int port)
        {
            _port = port;
            //Context.System.EventStream.Subscribe(Self, typeof(Terminated));

            var actorRef = Context.ActorSelection($"akka.tcp://akkatest@127.0.0.1:{port}/user/ping").ResolveOne(TimeSpan.FromSeconds(1)).Result;
            Context.Watch(actorRef);

           //Receive<Terminated>(m => HandleTerminated(m));
        }

        private void HandleTerminated(Terminated message)
        {
            Console.WriteLine($"{message.ActorRef.Path.Address} terminated");
            Context.Stop(Self);
        }

        protected override void PostStop()
        {
            Console.WriteLine($"watcher{_port} stopped");

        }
    }

    public class ReliableWatcher : ReceiveActor
    {
        private readonly int _port;

        public ReliableWatcher(int port)
        {
            _port = port;

            var actorRef = Context.ActorSelection($"akka.tcp://akkatest@127.0.0.1:{port}/user/ping").ResolveOne(TimeSpan.FromSeconds(1)).Result;
            Context.Watch(actorRef);

            Receive<Terminated>(m => HandleTerminated(m));
            Receive<string>(x => x == "Ping", m => TryPing());
        }

        private async void TryPing()
        {
            var actorRefTask = Context.ActorSelection($"akka.tcp://akkatest@127.0.0.1:{_port}/user/ping").ResolveOne(TimeSpan.FromSeconds(1));

            try
            {
                var actorRef = actorRefTask.Result;
                var pong = actorRef.Ask<string>("ping").Result;
                Console.Write("Starting watcher because of: " + pong);
                Context.Watch(actorRef);
            }
            catch (Exception)
            {
                Console.WriteLine("Failed pinging " + _port);
                Context.System.Scheduler.ScheduleTellOnceCancelable(TimeSpan.FromSeconds(1), Self, "Ping", Self);
            }
            
        }

        private void HandleTerminated(Terminated message)
        {
            Console.WriteLine($"{message.ActorRef.Path.Address} terminated");

            Context.System.Scheduler.ScheduleTellOnceCancelable(TimeSpan.FromSeconds(1), Self, "Ping", Self);
        }

        protected override void PostStop()
        {
            Console.WriteLine($"watcher{_port} stopped");

        }
    }

    public class PingActor : ReceiveActor
    {
        public PingActor()
        {
            Receive<string>(x => x == "ping", s =>
            {
                Sender.Tell("pong from " + Self.Path.Address.ToString());
            });
        }
    }
}
