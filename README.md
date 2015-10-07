#Akka.NET Cluster Test

The test project can be started with the following syntax

```
> .\AkkaTest.exe <HostPort> [<SeedNode1Port>], [<SeedNode2Port>], [<SeedNodeNPort>]
```

When the process is running its possible to issue a few different commands:

`join <NodePort>` will issue a join request to the cluster

`join-seed [<SeedNode1Port>] [<SeedNode2Port>] [<SeedNodeNPort>]` will issue a join seed nodes request to the cluster

`leave <NodePort>` will issue a leave request for the node specified

`down <NodePort>` will issue a down request for the node specified

`status` will output a status overview of the cluster from current nodes perspective

`ping <NodePort>` will send a ping to a simple `PingActor` on the specified node and print out the response.
