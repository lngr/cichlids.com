#:package AWSSDK.S3@4.0.100.2
// Creates or tears down a bucket on the local RustFS instance for write-stack.sh. The dev API
// only ensures its own configured bucket exists at startup (see S3ObjectStore.EnsureBucketAsync
// in app/api), so a second, test-only bucket needs a direct S3 client instead; the ETL project's
// bucket setup is not reusable here because it reads "ObjectStorage" only from its own
// appsettings.json, never from environment variables, so it cannot be pointed at a different
// bucket without editing that file. This is a .NET 10 file-based app (no .csproj) so the write
// stack does not need a second compiled project for a one-off S3 call.
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;

if (args.Length != 2 || args[0] is not ("ensure" or "delete"))
{
    Console.Error.WriteLine("Usage: dotnet run bucket-tool.cs -- <ensure|delete> <bucket>");
    return 2;
}

var action = args[0];
var bucket = args[1];

// The one bucket this tool is ever allowed to touch, mirroring the database name guard in
// write-stack.sh: a bug or a stray manual invocation must never be able to reach the seeded dev
// bucket (cichlids-media, holding the migrated legacy media) through this tool.
if (bucket != "cichlids-e2e")
{
    Console.Error.WriteLine($"Refusing to touch bucket '{bucket}': only cichlids-e2e is allowed here.");
    return 1;
}

// Local RustFS defaults from app/stack/compose.yaml; overridable for a differently configured
// stack. The secret key is the fixed local-dev value already committed in compose.yaml.
var serviceUrl = Environment.GetEnvironmentVariable("RUSTFS_SERVICE_URL") ?? "http://127.0.0.1:9000";
var accessKey = Environment.GetEnvironmentVariable("RUSTFS_ACCESS_KEY") ?? "cichlids";
var secretKey = Environment.GetEnvironmentVariable("RUSTFS_SECRET_KEY") ?? "cichlids-dev-secret"; // gitleaks:allow

var config = new AmazonS3Config
{
    ServiceURL = serviceUrl,
    ForcePathStyle = true,
    AuthenticationRegion = "us-east-1",
};
var client = new AmazonS3Client(new BasicAWSCredentials(accessKey, secretKey), config);

if (action == "ensure")
{
    if (!await AmazonS3Util.DoesS3BucketExistV2Async(client, bucket))
    {
        await client.PutBucketAsync(new PutBucketRequest { BucketName = bucket });
        Console.WriteLine($"Created bucket '{bucket}'.");
    }
    else
    {
        Console.WriteLine($"Bucket '{bucket}' already exists.");
    }

    // The same public-read policy the ETL applies to the dev bucket (see
    // S3ObjectStore.EnsurePublicReadPolicyAsync in app/api): the app loads media URLs straight
    // from the bucket, so without it every uploaded image answers 403.
    var policy = $$"""
        {
          "Version": "2012-10-17",
          "Statement": [
            {
              "Effect": "Allow",
              "Principal": "*",
              "Action": ["s3:GetObject"],
              "Resource": ["arn:aws:s3:::{{bucket}}/*"]
            }
          ]
        }
        """;
    await client.PutBucketPolicyAsync(new PutBucketPolicyRequest { BucketName = bucket, Policy = policy });
    Console.WriteLine($"Applied the public-read policy to bucket '{bucket}'.");

    return 0;
}

// action == "delete": empty the bucket (S3 refuses to delete a non-empty one), then remove it.
// Deleting a bucket that does not exist is not an error, so a partial or repeated teardown stays
// safe to rerun.
if (!await AmazonS3Util.DoesS3BucketExistV2Async(client, bucket))
{
    Console.WriteLine($"Bucket '{bucket}' does not exist, nothing to delete.");
    return 0;
}

string? continuationToken = null;
do
{
    var page = await client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = bucket, ContinuationToken = continuationToken });
    if (page.S3Objects is { Count: > 0 })
    {
        await client.DeleteObjectsAsync(new DeleteObjectsRequest
        {
            BucketName = bucket,
            Objects = page.S3Objects.Select(o => new KeyVersion { Key = o.Key }).ToList(),
        });
    }

    continuationToken = page.IsTruncated == true ? page.NextContinuationToken : null;
}
while (continuationToken is not null);

await client.DeleteBucketAsync(bucket);
Console.WriteLine($"Deleted bucket '{bucket}'.");
return 0;
