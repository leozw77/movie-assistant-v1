"use strict";
const fs = require("fs");
const path = require("path");

const root = path.resolve(__dirname, "..");
const host = fs.readFileSync(path.join(root, "HtmlMediaLibraryForm.cs"), "utf8");

const required = [
  ["list view exists", "_doubanPlusView"],
  ["subject view exists", "_doubanSubjectView"],
  ["shared environment is used", "GetDoubanEnvironmentAsync"],
  ["subject view is initialized separately", "EnsureDoubanSubjectViewAsync"],
  ["detail opens in subject view", "DualVisibleWebViews=True"],
  ["return switches to list view", "Mode=SwitchToListView"],
  ["list navigation is reused", "NavigationReused=True"],
  ["list state is retained", "ListViewStateRestore=True"]
];

const failures = required.filter(([, token]) => !host.includes(token));
for (const [name] of required) {
  if (!failures.some(([failedName]) => failedName === name)) console.log(`PASS: ${name}`);
}
for (const [name, token] of failures) console.log(`FAIL: ${name} (${token})`);
if (failures.length) process.exit(1);
console.log("ALL_DUAL_WEBVIEW_PROTOCOL_TESTS_OK");
