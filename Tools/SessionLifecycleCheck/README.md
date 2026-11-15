# Session lifecycle checks

Run `dotnet run --project Tools/SessionLifecycleCheck/SessionLifecycleCheck.csproj` from the repository root (.NET 10).

The executable compiles the production `UgsSessionService` against fake Multiplayer and Netcode boundaries. It checks host deletion, guest leave, duplicate operations, leaving during pending create/join, retry after failed deletion, deferred network shutdown, remote deletion, and starting a fresh room.

These checks do not connect to UGS or replace the two-player Unity checks:

1. Host + guest enter a match, surrender, then return in both orders (host first and guest first).
2. Create/join a second match without restarting either application; verify a new match ID and clean initial state.
3. Leave the waiting room while Create/Join is pending; verify no late room remains.
4. Close the host's room while the guest waits; verify both return to the menu.
5. Disconnect a guest during play and rejoin within 30 seconds; verify reconnect still works.
6. Interrupt connectivity during cleanup; verify retry is offered and a new room cannot bypass failed cleanup.

`[SessionLifecycle] Room left and network stopped` is emitted only after teardown completes, before loading MainMenu.
