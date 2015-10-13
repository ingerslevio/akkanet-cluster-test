using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
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
            var level = "ERROR";

            var config = @"
                akka {
                    stdout-loglevel = " + level + @"
                    loglevel = " + level + @"
                    log-config-on-start = off

                    actor {
                        provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
                    }

                    remote {
                        log-remote-lifecycle-events = " + level + @"
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
                    case "break-local-ping":
                        BreakLocalPing(pingActor, "1".Equals(commandArgs.Skip(1).FirstOrDefault()));
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

        private static void BreakLocalPing(IActorRef pingActorRef, bool forGood)
        {
            if (forGood)
            {
                pingActorRef.Tell(PoisonPill.Instance);
            }
            else
            {
                pingActorRef.Tell(new PingActor.Boom());
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
                .Ask<PingActor.Pong>(new PingActor.Ping())
                .ContinueWith(x =>
                {
                    if (x.IsFaulted)
                    {
                        Console.WriteLine("Got faulty ping response: " + x.Exception?.ToString());
                    }
                    else
                    {
                        Console.WriteLine("Got pong from: " + x.Result.From);
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

            Self.Tell(new TryNow());

            Receive<Terminated>(m => HandleTerminated(m));
            Receive((Action<PleasePing>) Handle);
            Receive((Action<TryAgainLater>) Handle);
            Receive((Action<TryNow>) Handle);
            Receive((Action<StartWatching>)Handle);
        }

        private void Handle(StartWatching message)
        {
            Console.Write("Starting watcher on " + message.ActorRef.ToString());
            Context.Watch(message.ActorRef);
        }
        
        private void Handle(TryAgainLater message)
        {
            Console.WriteLine("Trying later because of " + message.Why);
            Context.System.Scheduler.ScheduleTellOnceCancelable(TimeSpan.FromSeconds(1), Self, new TryNow(), Self);
        }

        private void Handle(TryNow message)
        {
            WriteOutput($"Trying to resolve PingActor");
            Context.ActorSelection($"akka.tcp://akkatest@127.0.0.1:{_port}/user/ping")
                .ResolveOne(TimeSpan.FromSeconds(1))
                .PipeTo2(Self, Self,
                    success: (r) => new PleasePing(r),
                    failure: (e) => new TryAgainLater(e.ToString()));
        }

        private void Handle(PleasePing message)
        {
            WriteOutput($"Trying to ping");
            message.ActorRef.Ask<PingActor.Pong>(new PingActor.Ping())
                .PipeTo2(Self, Self,
                    success: x => new StartWatching(x.From),
                    failure: e => new TryAgainLater(e.ToString()));
        }

        private void HandleTerminated(Terminated message)
        {
            WriteOutput($"Got termination of {message.ActorRef.Path.Address} message.");
            Self.Tell(new TryAgainLater("termination received"));
        }

        protected override void PostStop()
        {
            WriteOutput($"watcher{_port} stopped");
        }

        private void WriteOutput(string message)
        {
            Console.WriteLine($"[watcher{_port}] {message}");
        }
        
        public class PleasePing
        {
            public IActorRef ActorRef { get; }

            public PleasePing(IActorRef actorRef)
            {
                ActorRef = actorRef;
            }
        }

        public class TryAgainLater
        {
            public string Why { get; }

            public TryAgainLater(string why)
            {
                Why = why;
            }
        }

        public class TryNow {}

        public class StartWatching
        {
            public IActorRef ActorRef { get; }

            public StartWatching(IActorRef actorRef)
            {
                ActorRef = actorRef;
            }
        }
    }

    public class PingActor : ReceiveActor
    {
        public PingActor()
        {
            Console.WriteLine($"pinger starting");

            Receive<Ping>(s =>
            {
                Console.WriteLine("Got ping from " + Sender.ToString());
                Sender.Tell(new Pong(Self));
            });

            Receive((Action<Boom>) Handle);
        }

        private void Handle(Boom obj)
        {
            Console.WriteLine("got killer message from " + Sender.ToString());
            throw new Exception("Boom!");
        }

        protected override void PostStop()
        {
            Console.WriteLine($"pinger stopped");
        }

        public class Ping
        {
            
        }

        public class Pong
        {
            public IActorRef From { get; }

            public Pong(IActorRef from)
            {
                From = @from;
            }
        }

        public class Boom
        {
            
        }
    }
}
