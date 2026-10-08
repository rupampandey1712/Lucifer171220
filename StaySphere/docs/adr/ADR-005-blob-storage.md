# ADR-005: Blob storage for media with quarantine pipeline

- **Status:** Accepted (implemented)
- **Decision:** `IFileStorageService` (Upload, Download, Delete, GetReadUrl) is implemented with Azure.Storage.Blobs, using Azurite locally. Uploads land in `uploads-quarantine`. A worker validates them (magic bytes, size, dimensions), scans through `IMalwareScanner`, re-encodes to strip metadata, generates thumbnails, and moves them to `property-images`. Images are served via CDN using user-delegation SAS or a public read-only container behind Front Door.
- **Consequences:** Untrusted bytes are never served. Image processing scales independently.
