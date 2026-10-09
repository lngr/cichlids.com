import { describe, expect, it } from "vitest";
import {
  DEFAULT_TOPIC,
  TITLE_MAX_LENGTH,
  UPLOAD_TOPICS,
  canPublish,
  initialUploadForm,
  toPublishRequest,
  toUploadFile,
  uploadErrorMessage,
} from "./uploadForm";

describe("initialUploadForm", () => {
  it("starts with an empty title and description and the default topic", () => {
    expect(initialUploadForm()).toEqual({ title: "", description: "", topic: "cichlids" });
    expect(DEFAULT_TOPIC).toBe("cichlids");
  });

  it("offers exactly the publishable topics", () => {
    expect(UPLOAD_TOPICS).toEqual(["cichlids", "tanks", "offtopic"]);
  });
});

describe("canPublish", () => {
  it("rejects an empty title", () => {
    expect(canPublish({ ...initialUploadForm(), title: "" })).toBe(false);
  });

  it("rejects a title of whitespace only", () => {
    expect(canPublish({ ...initialUploadForm(), title: "   \n\t " })).toBe(false);
  });

  it("accepts a non-empty title", () => {
    expect(canPublish({ ...initialUploadForm(), title: "Tropheus moorii" })).toBe(true);
  });

  it("accepts a title of exactly the maximum length", () => {
    expect(TITLE_MAX_LENGTH).toBe(200);
    expect(canPublish({ ...initialUploadForm(), title: "a".repeat(200) })).toBe(true);
  });

  it("rejects a title longer than the maximum length", () => {
    expect(canPublish({ ...initialUploadForm(), title: "a".repeat(201) })).toBe(false);
  });

  it("measures the length after trimming", () => {
    expect(canPublish({ ...initialUploadForm(), title: `  ${"a".repeat(200)}  ` })).toBe(true);
  });
});

describe("toPublishRequest", () => {
  it("trims the title and sends null for a blank description", () => {
    expect(toPublishRequest({ title: "  Frontosa  ", description: "   ", topic: "tanks" })).toEqual({
      title: "Frontosa",
      description: null,
      topic: "tanks",
    });
  });

  it("keeps a trimmed description", () => {
    expect(toPublishRequest({ title: "Frontosa", description: " Kapampa \n", topic: "cichlids" })).toEqual({
      title: "Frontosa",
      description: "Kapampa",
      topic: "cichlids",
    });
  });
});

describe("toUploadFile", () => {
  it("passes uri, file name and MIME type of a picked asset", () => {
    expect(toUploadFile({ uri: "file:///a/IMG_1.jpg", fileName: "IMG_1.jpg", mimeType: "image/jpeg" })).toEqual({
      uri: "file:///a/IMG_1.jpg",
      name: "IMG_1.jpg",
      type: "image/jpeg",
    });
  });

  it("falls back to a JPEG name and type when the picker omits them", () => {
    expect(toUploadFile({ uri: "content://media/42", fileName: null, mimeType: undefined })).toEqual({
      uri: "content://media/42",
      name: "photo.jpg",
      type: "image/jpeg",
    });
  });
});

describe("uploadErrorMessage", () => {
  it("shows the API message of a 400 response", () => {
    const error = Object.assign(new Error("The file must be image/jpeg, image/png or image/webp."), { status: 400 });
    expect(uploadErrorMessage(error, "fallback")).toBe("The file must be image/jpeg, image/png or image/webp.");
  });

  it("uses the fallback for other failures", () => {
    const serverError = Object.assign(new Error("Internal Server Error"), { status: 500 });
    expect(uploadErrorMessage(serverError, "fallback")).toBe("fallback");
    expect(uploadErrorMessage(new TypeError("Failed to fetch"), "fallback")).toBe("fallback");
    expect(uploadErrorMessage("boom", "fallback")).toBe("fallback");
  });

  it("uses the fallback for a 400 response without a message", () => {
    expect(uploadErrorMessage(Object.assign(new Error(""), { status: 400 }), "fallback")).toBe("fallback");
  });
});
