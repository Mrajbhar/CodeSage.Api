using CodeSage.Api.Data;
using CodeSage.Api.Dtos;
using CodeSage.Api.Models;
using MongoDB.Driver;

namespace CodeSage.Api.Services;

// Phase 4 #4: builds and queries semantic vectors for a repo's files.
public class IndexService
{
    private readonly MongoContext _db;
    private readonly EmbeddingService _embed;
    private readonly GitHubService _github;
    private const int MaxFiles = 40;   // keep indexing bounded for local embedding models

    public IndexService(MongoContext db, EmbeddingService embed, GitHubService github)
    {
        _db = db; _embed = embed; _github = github;
    }

    // Runs as a Hangfire background job (#2). Embeds file paths for one repo.
    public async Task IndexRepoAsync(string orgId, string githubToken, string fullName)
    {
        var files = await _github.GetTreeAsync(githubToken, fullName);
        var slice = files.Take(MaxFiles).ToList();

        // wipe any previous index for this repo+org
        await _db.Embeddings.DeleteManyAsync(e => e.OrgId == orgId && e.RepoFullName == fullName);

        foreach (var f in slice)
        {
            float[] vector;
            try { vector = await _embed.EmbedAsync(f.Path); }
            catch { continue; }   // skip a file the model choked on, keep indexing the rest

            await _db.Embeddings.InsertOneAsync(new Embedding
            {
                OrgId = orgId,
                RepoFullName = fullName,
                Path = f.Path,
                Vector = vector
            });
        }
    }

    public async Task<List<SearchResultDto>> SearchAsync(string orgId, string fullName, string query, int top = 10)
    {
        var qvec = await _embed.EmbedAsync(query);
        var docs = await _db.Embeddings.Find(e => e.OrgId == orgId && e.RepoFullName == fullName).ToListAsync();

        return docs
            .Select(d => new SearchResultDto(d.Path, d.RepoFullName, Cosine(qvec, d.Vector)))
            .OrderByDescending(r => r.Score)
            .Take(top)
            .ToList();
    }

    private static double Cosine(float[] a, float[] b)
    {
        if (a.Length == 0 || b.Length == 0 || a.Length != b.Length) return 0;
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < a.Length; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        return (na == 0 || nb == 0) ? 0 : dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }
}