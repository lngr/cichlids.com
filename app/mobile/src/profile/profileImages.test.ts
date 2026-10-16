import { expect, it } from "vitest";
import { PROFILE_IMAGE_ROLES, profileImageNoneKey, profileImageUrl } from "./profileImages";

const profile = { avatarUrl: "https://cdn.test/avatar.jpg", profileImageUrl: null };

it("reads the avatar and the header from the profile", () => {
  expect(profileImageUrl(profile, "avatar")).toBe("https://cdn.test/avatar.jpg");
  expect(profileImageUrl(profile, "header")).toBeNull();
  expect(profileImageUrl({ avatarUrl: null, profileImageUrl: "https://cdn.test/header.jpg" }, "header")).toBe("https://cdn.test/header.jpg");
});

it("edits the avatar first and the header second, each with its own hint", () => {
  expect(PROFILE_IMAGE_ROLES).toEqual(["avatar", "header"]);
  expect(profileImageNoneKey("avatar")).toBe("profileImages.avatarNone");
  expect(profileImageNoneKey("header")).toBe("profileImages.headerNone");
});
