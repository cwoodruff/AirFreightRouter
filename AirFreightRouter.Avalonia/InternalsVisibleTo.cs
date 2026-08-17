using System.Runtime.CompilerServices;

// Expose internal members (e.g. RouteSolver.Factorial) to the unit-test project.
[assembly: InternalsVisibleTo("AirFreightRouter.Tests")]
