using System.Text.Json.Serialization;
using Ascolotl.Core.Models;

namespace Ascolotl.Infrastructure.Services;

/// <summary>Compile-time JSON metadata for the settings, session and recovery files, so reading them at
/// startup needs no reflection. Each store keeps its own options: the files are written as before.</summary>
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(SessionState))]
[JsonSerializable(typeof(List<RecoveryEntry>))]
internal sealed partial class StoreJson : JsonSerializerContext
{
}
