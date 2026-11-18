import { test } from "node:test";
import assert from "node:assert/strict";
import worker from "./worker.js";

const env = { GROQ_API_KEY: "test-key", GENERATION_LIMIT: { limit: async () => ({ success: true }) } };

function request(body) {
  return new Request("https://example.com/generate", { method: "POST", body: JSON.stringify(body) });
}

function mockProvider(t, result, check = () => {}) {
  return t.mock.method(globalThis, "fetch", async (_url, options) => {
    check(JSON.parse(options.body));
    return Response.json({ choices: [{ message: { content: JSON.stringify(result) } }] });
  });
}

test("returns only the generated fragment and sends only the chat revision", async t => {
  const user = { message: "Shorten it", previousText: "A longer fragment." };
  mockProvider(t, { text: "A short fragment.", question: null }, providerRequest => {
    assert.deepEqual(JSON.parse(providerRequest.messages[1].content), user);
    assert.equal(providerRequest.response_format.json_schema.strict, true);
  });
  const response = await worker.fetch(request({ ...user, kind: "document_text" }), env);
  assert.equal(response.status, 200);
  assert.deepEqual(await response.json(), { text: "A short fragment.", question: null });
});

test("returns a clarification separately from a fragment", async t => {
  mockProvider(t, { text: null, question: "What happened?" });
  const response = await worker.fetch(request({ message: "Write an explanation", kind: "explanation" }), env);
  assert.equal(response.status, 200);
  assert.deepEqual(await response.json(), { text: null, question: "What happened?" });
});

test("rejects document fields and profile data at the API boundary", async t => {
  const upstream = mockProvider(t, {});
  const response = await worker.fetch(request({
    message: "Draft an explanation", kind: "explanation", fields: { name: "private" },
  }), env);
  assert.equal(response.status, 400);
  assert.equal(upstream.mock.callCount(), 0);
});

test("rejects a result containing both a question and a fragment", async t => {
  mockProvider(t, { text: "Draft", question: "Question?" });
  const response = await worker.fetch(request({ message: "Draft an explanation", kind: "explanation" }), env);
  assert.equal(response.status, 502);
});

test("rejects an oversized body without Content-Length", async () => {
  const response = await worker.fetch(request({ message: "a".repeat(65 * 1024), kind: "explanation" }), env);
  assert.equal(response.status, 413);
});

test("reports unconfigured, rate-limited and unavailable providers", async t => {
  const body = { message: "Draft an explanation", kind: "explanation" };
  assert.equal((await worker.fetch(request(body), {})).status, 503);
  const limited = { ...env, GENERATION_LIMIT: { limit: async () => ({ success: false }) } };
  assert.equal((await worker.fetch(request(body), limited)).status, 429);
  t.mock.method(globalThis, "fetch", async () => new Response(null, { status: 503 }));
  assert.equal((await worker.fetch(request(body), env)).status, 503);
});

test("uses Workers-compatible manual redirects and rejects an upstream redirect", async t => {
  const upstream = t.mock.method(globalThis, "fetch", async (_url, options) => {
    assert.equal(options.redirect, "manual");
    return Response.redirect("https://example.com/redirect", 302);
  });
  const response = await worker.fetch(request({ message: "Draft an explanation", kind: "explanation" }), env);
  assert.equal(response.status, 503);
  assert.equal(upstream.mock.callCount(), 1);
});
