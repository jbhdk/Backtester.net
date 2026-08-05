using System.Runtime.CompilerServices;

// Engine-internal machinery stays internal and is asserted on directly from the test project
// (ADR 0033). This must be a hand-written attribute: the project sets GenerateAssemblyInfo=false,
// so the MSBuild <InternalsVisibleTo> item produces nothing and is silently inert.
[assembly: InternalsVisibleTo("BacktesterTests")]
