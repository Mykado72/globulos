// ✨ Plugin WebGL : ouvre une boîte de dialogue window.prompt() native du navigateur.
// Contrairement au TMP_InputField (dessiné dans le <canvas> Unity, pas un vrai élément HTML),
// window.prompt() est géré directement par le navigateur : il ouvre TOUJOURS le clavier virtuel
// sur mobile, sans les soucis de timing de focus() qu'un <input> HTML superposé peut avoir
// (notamment sur iOS Safari, très strict sur le "user gesture").
//
// Placer ce fichier dans Assets/Plugins/WebGL/ (peu importe le sous-dossier exact, Unity détecte
// les .jslib par leur emplacement sous Assets/Plugins/WebGL/).

mergeInto(LibraryManager.library, {

  WebGLPrompt_Show: function (messagePtr, defaultValuePtr) {
    var message = UTF8ToString(messagePtr);
    var defaultValue = UTF8ToString(defaultValuePtr);

    var result = window.prompt(message, defaultValue);

    // Annulé (Cancel) → on garde l'ancienne valeur plutôt que de renvoyer une chaîne vide
    if (result === null) {
      result = defaultValue;
    }

    // Un retour de type "string" côté C# doit être une chaîne allouée dans le tas WASM/asm.js
    var bufferSize = lengthBytesUTF8(result) + 1;
    var buffer = _malloc(bufferSize);
    stringToUTF8(result, buffer, bufferSize);
    return buffer;
  }

});
