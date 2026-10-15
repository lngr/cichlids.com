import { describe, expect, it } from "vitest";
import { readFileSync, readdirSync } from "node:fs";
import { resolve } from "node:path";
import ts from "typescript";
import i18next from "i18next";
import en from "./en.json";
import { resolveLanguage, SUPPORTED_LANGUAGES } from "./locales";
import { formatCount, formatDate } from "../lib/format";

function flatten(value: object, prefix = ""): Record<string, string> {
  return Object.fromEntries(Object.entries(value).flatMap(([key, item]) => {
    const path = prefix ? `${prefix}.${key}` : key;
    return typeof item === "string" ? [[path, item]] : Object.entries(flatten(item, path));
  }));
}
const base = flatten(en);
const placeholders = (text: string) => [...text.matchAll(/{{\s*([^}]+)\s*}}/g)].map((m) => m[1].trim()).sort();
const pluralBase = (key: string) => {
  const root = key.replace(/_(zero|one|two|few|many|other)$/, "");
  return `${root}_one` in base ? root : key;
};

describe("launch localization", () => {
  it("resolves device regions, language families and the English fallback", () => {
    expect(SUPPORTED_LANGUAGES).toEqual(["en", "es-419", "pt-BR", "de", "fr", "it", "pl", "nl", "cs", "id", "tr"]);
    for (const [input, expected] of [["pt-BR", "pt-BR"], ["pt-PT", "pt-BR"], ["es-MX", "es-419"], ["de-AT", "de"], ["fr-CA", "fr"], ["ar", "en"], [null, "en"]]) {
      expect(resolveLanguage(input)).toBe(expected);
    }
  });
  for (const language of ["en", "es-419", "pt-BR", "de", "fr", "it", "pl", "nl", "cs", "id", "tr"]) {
    it(`${language} has every key, matching placeholders and its Intl plural categories`, async () => {
      const catalogue = JSON.parse(readFileSync(new URL(`./${language}.json`, import.meta.url), "utf8"));
      const entries = flatten(catalogue);
      const categories = new Intl.PluralRules(language).resolvedOptions().pluralCategories;
      for (const [key, text] of Object.entries(base)) {
        const root = pluralBase(key);
        const keys = root === key ? [key] : categories.map((category) => `${root}_${category}`);
        for (const required of keys) {
          expect(entries[required], required).toBeTruthy();
          expect(placeholders(entries[required]), required).toEqual(placeholders(text));
        }
      }
      const instance = i18next.createInstance();
      await instance.init({ lng: language, fallbackLng: false, resources: { [language]: { translation: catalogue } } });
      for (const count of [0, 1, 2, 5, 21, 22, 101, 1.5]) {
        const category = new Intl.PluralRules(language).select(count);
        expect(instance.t("profile.pictures", { count })).toBe(entries[`profile.pictures_${category}`].replace("{{count}}", String(count)));
      }
    });
  }
  it("formats Brazilian and German numbers and dates with Intl", () => {
    for (const language of SUPPORTED_LANGUAGES) {
      expect(formatCount(12345, language)).toBe(new Intl.NumberFormat(language, { notation: "compact", maximumFractionDigits: 1 }).format(12345));
      expect(formatDate("2026-07-15T12:00:00Z", language)).toBe(new Intl.DateTimeFormat(language, { year: "numeric", month: "short", day: "numeric" }).format(new Date("2026-07-15T12:00:00Z")));
    }
  });
});

function uiViolations(source: string): string[] {
  const file = ts.createSourceFile("screen.tsx", source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
  const problems: string[] = [];
  function visit(node: ts.Node) {
    if (ts.isJsxText(node) && /\p{L}/u.test(node.text)) problems.push(node.text.trim());
    if (ts.isJsxAttribute(node) && /^(title|label|placeholder|accessibilityLabel|message)$/.test(node.name.getText(file))) {
      const value = node.initializer;
      if (value && ts.isStringLiteral(value) && /\p{L}/u.test(value.text)) problems.push(value.text);
    }
    if (ts.isPropertyAssignment(node) && /^(title|label|placeholder|accessibilityLabel|message)$/.test(node.name.getText(file)) && ts.isStringLiteral(node.initializer) && /\p{L}/u.test(node.initializer.text)) problems.push(node.initializer.text);
    if (ts.isJsxExpression(node) && node.expression) {
      function expressionLiterals(expression: ts.Node) {
        if (ts.isJsxElement(expression) || ts.isJsxSelfClosingElement(expression) || ts.isCallExpression(expression) || ts.isArrowFunction(expression)) return;
        if (ts.isConditionalExpression(expression)) {
          expressionLiterals(expression.whenTrue);
          expressionLiterals(expression.whenFalse);
          return;
        }
        if (ts.isBinaryExpression(expression)) {
          if ([ts.SyntaxKind.PlusToken, ts.SyntaxKind.BarBarToken, ts.SyntaxKind.QuestionQuestionToken].includes(expression.operatorToken.kind)) expressionLiterals(expression.left);
          if ([ts.SyntaxKind.PlusToken, ts.SyntaxKind.BarBarToken, ts.SyntaxKind.QuestionQuestionToken, ts.SyntaxKind.AmpersandAmpersandToken].includes(expression.operatorToken.kind)) expressionLiterals(expression.right);
          return;
        }
        if (ts.isStringLiteral(expression) && /\p{L}/u.test(expression.text)) problems.push(expression.text);
        if ((ts.isTemplateHead(expression) || ts.isTemplateMiddle(expression) || ts.isTemplateTail(expression) || ts.isNoSubstitutionTemplateLiteral(expression)) && /\p{L}/u.test(expression.text)) problems.push(expression.text);
        ts.forEachChild(expression, expressionLiterals);
      }
      if (ts.isJsxElement(node.parent) || ts.isJsxFragment(node.parent)) expressionLiterals(node.expression);
    }
    if (ts.isPropertyAssignment(node) && /^(left|right|marginLeft|marginRight|paddingLeft|paddingRight|borderTopLeftRadius|borderTopRightRadius)$/.test(node.name.getText(file))) problems.push(node.name.getText(file));
    ts.forEachChild(node, visit);
  }
  visit(file);
  return problems;
}
function screens(dir: string): string[] {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => entry.isDirectory() ? screens(resolve(dir, entry.name)) : entry.name.endsWith(".tsx") ? [resolve(dir, entry.name)] : []);
}
it("detects visible literals and physical layout properties", () => {
  expect(uiViolations('<Text>Hello</Text>')).toEqual(["Hello"]);
  expect(uiViolations('<Button label="Hello" />')).toEqual(["Hello"]);
  expect(uiViolations('<Text>{"Hello"}</Text>')).toEqual(["Hello"]);
  expect(uiViolations('<Text>{ready ? "Hello" : t("common.loading")}</Text>')).toEqual(["Hello"]);
  expect(uiViolations('<Text>{`Hello ${name}`}</Text>')).toEqual(["Hello "]);
  expect(uiViolations('const style = { marginLeft: 2 };')).toEqual(["marginLeft"]);
});
it("screens and shared components use translated text and logical layout properties", () => {
  const files = [...screens(resolve("app")), ...screens(resolve("src"))];
  expect(files.flatMap((file) => uiViolations(readFileSync(file, "utf8")).map((problem) => `${file}: ${problem}`))).toEqual([]);
});

it("static translation calls refer to known keys", () => {
  const files = [...screens(resolve("app")), ...screens(resolve("src"))];
  const keys = Object.keys(base);
  const missing = files.flatMap((file) => [...readFileSync(file, "utf8").matchAll(/\bt\(\s*["']([^"']+)["']/g)].map((match) => match[1]).filter((key) => !keys.includes(key) && !keys.some((candidate) => pluralBase(candidate) === key)).map((key) => `${file}: ${key}`));
  expect(missing).toEqual([]);
});
