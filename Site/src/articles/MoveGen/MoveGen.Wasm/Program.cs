// browser-wasm needs an entry point; the runtime keeps the module alive
// so the JS side can call [JSExport] methods after startup.

MoveGen.App.Magic.Init();
