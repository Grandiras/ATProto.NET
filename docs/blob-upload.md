# Blob Upload

Upload images, files, and binary data to a PDS for use in records.

## Basic Upload

`UploadBlobAsync` returns the `BlobRef` itself — store it on a record to reference the uploaded data.

```csharp
// From a file path
BlobRef blob = await client.Repo.UploadBlobAsync(
    filePath: "/path/to/image.jpg",
    mimeType: "image/jpeg");

Console.WriteLine($"Blob CID: {blob.Ref?.Link}, {blob.Size} bytes");
```

## Upload Methods

### From File Path

```csharp
BlobRef blob = await client.Repo.UploadBlobAsync(
    "/path/to/photo.png",
    "image/png");
```

### From Stream

```csharp
using var stream = File.OpenRead("/path/to/file.pdf");
BlobRef blob = await client.Repo.UploadBlobAsync(
    stream,
    "application/pdf");
```

### From Byte Array

```csharp
byte[] imageBytes = await DownloadImageAsync(url);
BlobRef blob = await client.Repo.UploadBlobAsync(
    imageBytes,
    "image/jpeg");
```

## Using Blobs in Records

After uploading, reference the blob in your record:

```csharp
public class PhotoRecord : AtProtoRecord
{
    [JsonPropertyName("$type")]
    public override string Type => "com.example.photos.photo";

    [JsonPropertyName("image")]
    public BlobRef? Image { get; set; }

    [JsonPropertyName("caption")]
    public string Caption { get; set; } = "";

    [JsonPropertyName("altText")]
    public string? AltText { get; set; }
}

// Upload then create record
BlobRef uploaded = await client.Repo.UploadBlobAsync(
    "/path/to/vacation.jpg",
    "image/jpeg");

var photos = client.GetCollection<PhotoRecord>("com.example.photos.photo");
await photos.CreateAsync(new PhotoRecord
{
    Image = uploaded,
    Caption = "Beach sunset",
    AltText = "A beautiful sunset over the ocean",
});
```

## Download Blobs

```csharp
await using var stream = await client.Sync.GetBlobAsync(
    "did:plc:abc123",
    "bafyreib...");

await using var file = File.Create("downloaded.jpg");
await stream.CopyToAsync(file);
```

## Size Limits

PDS implementations typically enforce blob size limits:
- Most PDS servers limit blobs to **1 MB** for images
- Video limits vary by PDS configuration
- Check your PDS documentation for specific limits

## MIME Types

Common MIME types for AT Protocol blobs:

| Format | MIME Type |
|--------|-----------|
| JPEG | `image/jpeg` |
| PNG | `image/png` |
| GIF | `image/gif` |
| WebP | `image/webp` |
| MP4 | `video/mp4` |
| PDF | `application/pdf` |
