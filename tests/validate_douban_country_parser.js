const fs = require("fs");
const vm = require("vm");

const sourcePath = require("path").join(__dirname, "..", "WebAssets", "DoubanPlus", "douban-source-bridge.js");
const labelsPath = require("path").join(__dirname, "..", "WebAssets", "DoubanPlus", "douban-country-labels.js");
const labels = fs.readFileSync(labelsPath, "utf8");
const source = fs.readFileSync(sourcePath, "utf8");
const exposed = source.replace(
  "window.QbDoubanSourceBridge = Object.freeze({ readPage, openFilterGroup, selectFilter, loadMore });",
  "window.QbDoubanSourceBridge = Object.freeze({ readPage, openFilterGroup, selectFilter, loadMore, __parseExploreMeta: parseExploreMeta });"
);
const window = {};
window.top = window;
const context = {
  window,
  location: { hostname: "movie.douban.com", pathname: "/explore", href: "https://movie.douban.com/explore" },
  document: {
    readyState: "complete",
    querySelector: () => null,
    querySelectorAll: () => [],
    addEventListener: () => {},
    documentElement: { outerHTML: "" },
    body: { textContent: "" }
  },
  URL,
  Set,
  String,
  Number,
  Boolean,
  console
};
vm.runInNewContext(`${labels}\n${exposed}`, context, { filename: sourcePath });

const parse = window.QbDoubanSourceBridge.__parseExploreMeta;
const cases = [
  ["2026 / 英国 法国 美国 / 动作 科幻 悬疑 惊悚 / 路易斯·莱特里尔 / 格蕾塔·李 瓦格纳·莫拉", "2026 / 英国 / 法国 / 美国", "路易斯·莱特里尔", ["格蕾塔·李", "瓦格纳·莫拉"]],
  ["2026 / 中国大陆 / 剧情 古装 / 邓科 / 丁禹兮 邓恩熙", "2026 / 中国大陆", "邓科", ["丁禹兮", "邓恩熙"]],
  ["2025 / 伊朗 / 剧情 / 导演甲 / 演员甲 演员乙", "2025 / 伊朗", "导演甲", ["演员甲", "演员乙"]]
];

for (const [subtitle, expectedIdentity, expectedDirector, expectedCast] of cases) {
  const actual = parse(subtitle);
  if (actual.identity !== expectedIdentity || actual.director !== expectedDirector || JSON.stringify(actual.cast) !== JSON.stringify(expectedCast)) {
    throw new Error(`explore parse mismatch: ${subtitle} -> ${JSON.stringify(actual)}; expected ${expectedIdentity}/${expectedDirector}/${JSON.stringify(expectedCast)}`);
  }
}

console.log(`Douban country parser regression: PASS (${cases.length}/${cases.length})`);
