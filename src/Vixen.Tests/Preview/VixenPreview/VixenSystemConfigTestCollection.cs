using Xunit;

namespace Vixen.Tests.Preview.VixenPreview;

/// <summary>
/// Groups tests that load the static <see cref="Vixen.Sys.VixenSystem"/> configuration so they do not race on it.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class VixenSystemConfigTestCollection
{
	public const string Name = "VixenSystem config tests";
}
