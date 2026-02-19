# Blob Upload

Upload images, files, and binary data to a PDS for use in records.

## Basic Upload

```csharp
// From a file path
var result = await client.Repo.UploadBlobAsync(
    filePath: "/path/to/image.jpg",
    mimeType: "image/jpeg");

Console.WriteLine($"Blob ref: {result.Blob}");
```

## Upload Methods

### From File Path

```csharp
var result = await client.Repo.UploadBlobAsync(
    "/path/to/photo.png",
    "image/png");
```

### From Stream

```csharp
using var stream = File.OpenRead("/path/to/file.pdf");
var result = await client.Repo.UploadBlobAsync(
    stream,
    "application/pdf");
```

### From Byte Array

```csharp
byte[] imageBytes = await DownloadImageAsync(url);
var result = await client.Repo.UploadBlobAsync(
    imageBytes,
    "image/jpeg");
```

## Using Blobs in Records

After uploading, reference the blob in your record:

```csharp
public class PhotoRecord : AtProtoRecord
{
    public override string Type => "com.example.photos.photo";

    [JsonPropertyName("image")]
    public BlobRef? Image { get; set; }

    [JsonPropertyName("caption")]
    public string Caption { get; set; } = "";

    [JsonPropertyName("altText")]
    public string? AltText { get; set; }
}

// Upload then create record
var uploadResult = await client.Repo.UploadBlobAsync(
    "/path/to/vacation.jpg",
    "image/jpeg");

var photos = client.GetCollection<PhotoRecord>("com.example.photos.photo");
await photos.CreateAsync(new PhotoRecord
{
    Image = uploadResult.Blob,
    Caption = "Beach sunset",
    AltText = "A beautiful sunset over the ocean",
});
```

## Download Blobs

```csharp
var (stream, contentType) = await client.Sync.DownloadBlobAsync(
    "did:plc:abc123",
    "bafyreib...");

using (stream)
{
    await using var file = File.Create("downloaded.jpg");
    await stream.CopyToAsync(file);
}
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
