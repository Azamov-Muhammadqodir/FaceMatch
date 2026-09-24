namespace FaceMatch.Core.Domain;

public static class FaceEmbedding
{
    /// <summary>Dimensionality of ArcFace embeddings stored in the database.</summary>
    public const int Dimensions = 512;

    /// <summary>Returns an L2-normalised copy of <paramref name="vector"/>.</summary>
    public static float[] Normalize(ReadOnlySpan<float> vector)
    {
        double sum = 0;
        foreach (var v in vector)
        {
            sum += v * v;
        }

        var norm = (float)Math.Sqrt(sum);
        var result = new float[vector.Length];
        if (norm <= float.Epsilon)
        {
            return result;
        }

        for (var i = 0; i < vector.Length; i++)
        {
            result[i] = vector[i] / norm;
        }

        return result;
    }

    /// <summary>Cosine similarity of two vectors (works for non-normalised input too).</summary>
    public static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length)
        {
            throw new ArgumentException("Vectors must have the same length.");
        }

        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }

        return na <= 0 || nb <= 0 ? 0 : (float)(dot / (Math.Sqrt(na) * Math.Sqrt(nb)));
    }
}
