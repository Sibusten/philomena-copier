# Philomena Copier
Copies images from one Philomena booru to another

Requires an account on both the source and target booru to use API keys.

Notes:

- The filter you have set on the source booru will be used when copying. Anything hidden will not be copied.
- Images that have already been uploaded will be skipped **if they are an exact match**. This is always the case if the image already came from the same source booru.
- Any query works, even `my:upvotes`, `my:uploads`, `my:watched`, etc.
