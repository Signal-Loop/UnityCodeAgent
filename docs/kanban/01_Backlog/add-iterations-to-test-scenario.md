WHen running scenarios, there should be iterations parameter to specify how many times the scenario should be executed.
Iterations should have default 1, be configurable in scenarios.toml, and be overridable in the command line.
Each iterattion shuould be isloated from each other - by session maybe.
All iteration should use same agent and same environment, to save time.
