# Philomena Copier
Copies images from one Philomena booru to another

Requires an account on both the source and target booru to use API keys.

Requires [.NET Core 3.1 Runtime](https://dotnet.microsoft.com/download/dotnet-core/3.1)

Notes:

- The filter you have set on the source booru will be used when copying. Anything hidden will not be copied.
- Images that have already been uploaded will be skipped **if they are an exact match**. This is always the case if the image already came from the same source booru.
- Any query works, even `my:upvotes`, `my:uploads`, `my:watched`, etc.
