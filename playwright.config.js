const { defineConfig } = require('@playwright/test');
module.exports = defineConfig({ testDir:'tests/browser', use:{ baseURL:'http://127.0.0.1:4174', trace:'retain-on-failure' }, webServer:{ command:'node tests/browser/fixture-server.js', url:'http://127.0.0.1:4174', reuseExistingServer:false } });
