using CodeSage.Api.Models;
using CodeSage.Api.Settings;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace CodeSage.Api.Data;

public class MongoContext
{
    public IMongoCollection<User> Users { get; }
    public IMongoCollection<Comment> Comments { get; }
    public IMongoCollection<Organization> Organizations { get; }
    public IMongoCollection<Usage> Usage { get; }
    public IMongoCollection<BillingEvent> BillingEvents { get; }
    public IMongoCollection<Review> Reviews { get; }
    public IMongoCollection<AuditLog> AuditLogs { get; }
    public IMongoCollection<Embedding> Embeddings { get; }
    public IMongoCollection<WatchedRepo> WatchedRepos { get; }
    public IMongoCollection<Notification> Notifications { get; }

    public MongoContext(IOptions<MongoDbSettings> options)
    {
        var url = new MongoUrl(options.Value.ConnectionString);
        var client = new MongoClient(url);
        // database name comes from the connection string (the path after the host),
        // falling back to "codesage" if the URL doesn't include one.
        var db = client.GetDatabase(string.IsNullOrWhiteSpace(url.DatabaseName) ? "codesage" : url.DatabaseName);

        Users = db.GetCollection<User>("users");
        Comments = db.GetCollection<Comment>("comments");
        Organizations = db.GetCollection<Organization>("organizations");
        Usage = db.GetCollection<Usage>("usage");
        BillingEvents = db.GetCollection<BillingEvent>("billingEvents");
        Reviews = db.GetCollection<Review>("reviews");
        AuditLogs = db.GetCollection<AuditLog>("auditLogs");
        Embeddings = db.GetCollection<Embedding>("embeddings");
        WatchedRepos = db.GetCollection<WatchedRepo>("watchedRepos");
        Notifications = db.GetCollection<Notification>("notifications");
        Notifications.Indexes.CreateOne(new CreateIndexModel<Notification>(
            Builders<Notification>.IndexKeys.Ascending(n => n.OrgId).Descending(n => n.CreatedAt)));

        WatchedRepos.Indexes.CreateOne(new CreateIndexModel<WatchedRepo>(
            Builders<WatchedRepo>.IndexKeys.Ascending(w => w.RepoFullName)));

        Embeddings.Indexes.CreateOne(new CreateIndexModel<Embedding>(
            Builders<Embedding>.IndexKeys.Ascending(e => e.OrgId).Ascending(e => e.RepoFullName)));

        AuditLogs.Indexes.CreateOne(new CreateIndexModel<AuditLog>(
            Builders<AuditLog>.IndexKeys.Ascending(a => a.OrgId).Descending(a => a.CreatedAt)));

        // recent reviews per org
        Reviews.Indexes.CreateOne(new CreateIndexModel<Review>(
            Builders<Review>.IndexKeys.Ascending(r => r.OrgId).Descending(r => r.CreatedAt)));

        // one usage doc per org per month
        Usage.Indexes.CreateOne(new CreateIndexModel<Usage>(
            Builders<Usage>.IndexKeys.Ascending(u => u.OrgId).Ascending(u => u.Period),
            new CreateIndexOptions { Unique = true }));

        // one account per email
        Users.Indexes.CreateOne(new CreateIndexModel<User>(
            Builders<User>.IndexKeys.Ascending(u => u.Email),
            new CreateIndexOptions { Unique = true }));

        // fetch a discussion thread quickly
        Comments.Indexes.CreateOne(new CreateIndexModel<Comment>(
            Builders<Comment>.IndexKeys.Ascending(c => c.Target).Ascending(c => c.CreatedAt)));
    }
}