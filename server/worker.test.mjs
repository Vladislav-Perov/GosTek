import assert from "node:assert/strict";
import { test } from "node:test";
import worker from "./worker.js";

function request(payload) {
  return new Request("https://test.example", {
    method: "POST",
    body: JSON.stringify(payload),
  });
}

const payload = {
  userText: "Scheduled maintenance",
  fieldsContext: "field_1 (Reason): empty",
  fieldIds: ["field_1"],
};

function providerAnswer(output) {
  return Response.json({ choices: [{ message: { content: JSON.stringify(output) } }] });
}

test("rejects oversized bodies without Content-Length", async () => {
  const response = await worker.fetch(request({ userText: "a".repeat(33 * 1024) }), {});
  assert.equal(response.status, 413);
});

test("reports a configuration error when no provider is configured", async () => {
  const response = await worker.fetch(request(payload), {});
  assert.equal(response.status, 503);
  assert.equal((await response.json()).code, "configuration");
});

test("uses a supported default and accepts a question without fields", async (t) => {
  t.mock.method(globalThis, "fetch", async (_url, options) => {
    assert.equal(JSON.parse(options.body).model, "openai/gpt-oss-20b");
    return providerAnswer({ fields: {}, question: "When did it happen?" });
  });
  const response = await worker.fetch(request(payload), { GROQ_API_KEY: "test" });
  assert.equal(response.status, 200);
  assert.equal((await response.json()).data.status, "need_info");
});

test("skips wrong types and unknown field IDs, then returns a valid fallback", async (t) => {
  const answers = [
    { fields: { field_1: 4 } },
    { fields: { field_2: "Unknown" } },
    { fields: { field_1: "Scheduled maintenance" } },
  ];
  const fetchMock = t.mock.method(globalThis, "fetch", async () => providerAnswer(answers.shift()));
  const env = {
    GROQ_API_KEY: "test",
    MODELS_CASCADE: JSON.stringify(["first", "second", "third"].map(model => ({ provider: "groq", model }))),
  };
  const response = await worker.fetch(request(payload), env);
  assert.equal(response.status, 200);
  assert.deepEqual((await response.json()).data.fields, { field_1: "Scheduled maintenance" });
  assert.equal(fetchMock.mock.callCount(), 3);
});

test("keeps the response format used by existing clients", async (t) => {
  t.mock.method(globalThis, "fetch", async () => providerAnswer({ fields: { Reason: "Maintenance" } }));
  const response = await worker.fetch(request({ userText: "Maintenance" }), { GROQ_API_KEY: "test" });
  assert.equal(response.status, 200);
  const result = await response.json();
  assert.deepEqual(JSON.parse(result.text).fields, { Reason: "Maintenance" });
});
