# Extractor integration contract

The Python boundary uses raw UTF-8 JSON messages. The command queue is
`youtube.extract-video.commands.v1`; the event queue is
`youtube.extract-video.events.v1`. Every message uses the versioned envelope
shown in the examples in this directory.

Consumers must reject unsupported `schemaVersion` or `messageType` values.
Delivery is at least once, so `messageId` is the idempotency key and
`correlationId` is the job ID. Payload field names are lower camel case.
