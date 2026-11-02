using Dhadgar.Lab.Data;
using Npgsql;
using NpgsqlTypes;

namespace Dhadgar.Lab.Tests.Data;

/// <summary>
/// <see cref="LabDb.OidArrayParameter"/>: the catalog lookup's OID list is bound as <c>oid[]</c>, not as a type Npgsql
/// cannot map (F143). No database is needed. The write itself is proven by <c>LabDbDockerTests</c>.
/// </summary>
public sealed class LabDbOidParameterTests
{
    [Fact]
    public void OidArrayParameter_IsTypedAsOidArray_SoNpgsqlCanWriteUInt32Values()
    {
        // uint.MaxValue: an OID above int.MaxValue is why the list is not bound as int[] (D-ML168).
        NpgsqlParameter<uint[]> parameter = LabDb.OidArrayParameter("oids", [16384u, uint.MaxValue]);

        Assert.Equal(NpgsqlDbType.Array | NpgsqlDbType.Oid, parameter.NpgsqlDbType);
        Assert.Equal("oids", parameter.ParameterName);
        Assert.Equal([16384u, uint.MaxValue], parameter.TypedValue!);
    }
}
