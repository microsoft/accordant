# Termination Detection (EWD998)

This project models the [termination detection](https://www.cs.utexas.edu/users/EWD/ewd09xx/EWD998.PDF)
protocol where the leader in a set of nodes detects whether
the distributed computation being conducted by the nodes has terminated. The problem is
made interesting as active nodes can send messages to passive nodes, thus "waking" them
again. Message sends and receives are asynchronous, though the specification does assume
that sent messages are always received, even if with an arbitrary delay.

This project shows how we can use Accordant to "model check" the above specification and
find safety and liveness bugs in the spec itself. While Accordant are typically used to
generate inputs and test cases to ensure the "system under test" meets the "spec", model checking
tests the spec itself to find design bugs and is a useful tool for more complex specs where it's hard
to reason about what the system _should_ do.

The specification below is a transcription of the specification of this protocol in [TLA+](https://lamport.azurewebsites.net/tla/tla.html). See the [TLA+ spec](https://github.com/lemmy/ewd998/blob/main/EWD998.tla) in this
[repository](https://github.com/lemmy/ewd998).