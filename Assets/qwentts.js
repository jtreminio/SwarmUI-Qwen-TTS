"use strict";
/// <reference path="./globals.d.ts" />
class QwenTTSUI {
    speakerOptions = [
        "Aiden",
        "Dylan",
        "Eric",
        "Ono_anna",
        "Ryan",
        "Serena",
        "Sohee",
        "Uncle_fu",
        "Vivian"
    ];
    voices = [];
    editor = null;
    voiceList = null;
    inputElem = null;
    genWrapInterval = null;
    voiceSyncTimer = null;
    voiceIdCounter = 0;
    constructor() {
        this.registerAudioPromptPrefix();
        this.waitForInit();
        this.startGenerateWrapRetry();
    }
    registerAudioPromptPrefix() {
        promptTabComplete.registerPrefix("audio", "Add a section of prompt text used as the dialogue script for Qwen-TTS.", () => [
            '\nUse "<audio>..." to provide the dialogue script.',
            "\nThe script should be formatted as RoleName: Dialogue line.",
        ], true);
    }
    waitForInit() {
        const checkInterval = setInterval(() => {
            if (!this.tryInit()) {
                return;
            }
            clearInterval(checkInterval);
        }, 100);
    }
    tryInit() {
        if (typeof postParamBuildSteps === "undefined" || !Array.isArray(postParamBuildSteps)) {
            return false;
        }
        postParamBuildSteps.push(() => {
            try {
                this.init();
            }
            catch (e) {
                console.log("Qwen-TTS: failed to build UI", e);
            }
        });
        return true;
    }
    init() {
        // SwarmUI removes underscores from parameter names: "qwen_tts_voices" -> "qwenttsvoices"
        this.inputElem = document.getElementById("input_qwenttsvoices");
        if (!this.inputElem) {
            console.log("Qwen-TTS: Could not find voices input element. Tried: input_qwenttsvoices");
            return;
        }
        // SwarmUI converts "Qwen-TTS" group name to "qwentts" (lowercase, hyphen removed)
        const groupContent = document.getElementById("input_group_content_qwentts");
        if (!groupContent) {
            console.log("Qwen-TTS: Could not find group content element. Tried: input_group_content_qwentts");
            return;
        }
        // Insert voices list below "Qwen-TTS Model" and "Qwen-TTS Use in Video" (param id: input_qwenttsmodel, input_qwenttsuseinvideo)
        const useInVideoInput = document.getElementById("input_qwenttsuseinvideo");
        let insertBeforeNode = null;
        if (useInVideoInput) {
            let block = useInVideoInput;
            while (block && block.parentElement !== groupContent) {
                block = block.parentElement;
            }
            insertBeforeNode = block ? block.nextSibling : null;
        }
        if (!insertBeforeNode && document.getElementById("input_qwenttsmodel")) {
            const modelBlock = document.getElementById("input_qwenttsmodel")?.closest?.(".input-group");
            insertBeforeNode = modelBlock?.nextSibling ?? null;
        }
        if (!insertBeforeNode) {
            insertBeforeNode = groupContent.firstChild;
        }
        let editor = document.getElementById("qwentts_audio_editor");
        if (!editor) {
            editor = document.createElement("div");
            editor.id = "qwentts_audio_editor";
            editor.className = "qwentts-audio-editor keep_group_visible";
            groupContent.insertBefore(editor, insertBeforeNode);
        }
        else {
            groupContent.insertBefore(editor, insertBeforeNode);
        }
        this.editor = editor;
        this.loadVoicesFromInput();
        this.render();
    }
    nextVoiceId() {
        this.voiceIdCounter += 1;
        return `${Date.now()}_${this.voiceIdCounter}`;
    }
    loadVoicesFromInput() {
        this.voices = [];
        if (!this.inputElem) {
            return;
        }
        try {
            const parsed = JSON.parse(this.inputElem.value || "[]");
            if (Array.isArray(parsed)) {
                this.voices = parsed.map((raw) => this.normalizeVoice(raw));
            }
        }
        catch {
            this.voices = [];
        }
    }
    normalizeVoice(raw) {
        const type = raw.type ?? "custom";
        const speaker = raw.speaker || "Serena";
        const name = raw.name || (type === "custom" ? speaker : type === "design" ? "Voice Design" : "Audio Voice");
        return {
            id: raw.id || this.nextVoiceId(),
            type,
            name,
            referenceText: raw.referenceText || "",
            styleInstruction: raw.styleInstruction || "",
            speaker,
            audioBase64: raw.audioBase64 || "",
            audioFileName: raw.audioFileName || "",
        };
    }
    render() {
        if (!this.editor || !this.editor.isConnected) {
            this.editor = null;
            this.voiceList = null;
            this.init();
            return;
        }
        let list = this.voiceList;
        if (!list || !list.isConnected) {
            list = document.createElement("div");
            list.id = "qwentts_voice_list";
            this.editor.appendChild(list);
            this.voiceList = list;
            this.installChangeListener();
        }
        list.innerHTML = "";
        this.voices.forEach((voice) => {
            list.appendChild(this.buildVoiceSection(voice));
        });
        this.editor.querySelector(".qwentts-add-buttons")?.remove();
        if (this.isFeatureInstalled()) {
            this.editor.appendChild(this.buildAddButtons());
        }
    }
    getVoiceTypeLabel(type) {
        switch (type) {
            case "custom": return "Custom Voice";
            case "design": return "Voice Design";
            case "audio": return "Audio File";
            default: return "Voice";
        }
    }
    isFeatureInstalled() {
        if (!Array.isArray(currentBackendFeatureSet)) {
            return false;
        }
        return currentBackendFeatureSet.includes("qwen_tts");
    }
    buildVoiceSection(voice) {
        const wrap = document.createElement("div");
        wrap.className = "input-group input-group-open border rounded p-2 mb-2";
        wrap.dataset.qwenttsVoiceId = voice.id;
        const header = document.createElement("span");
        header.className = "input-group-header input-group-noshrink";
        const typeLabel = this.getVoiceTypeLabel(voice.type);
        header.innerHTML =
            `<span class="header-label-wrap">`
                + `<span class="header-label">${this.escapeHtml(typeLabel)}</span>`
                + `<span class="header-label-spacer"></span>`
                + `<button class="interrupt-button" title="Remove voice" data-qwentts-action="remove-voice">×</button>`
                + `</span>`;
        wrap.appendChild(header);
        const content = document.createElement("div");
        content.className = "input-group-content";
        wrap.appendChild(content);
        const prefix = `qwentts_voice_${voice.id}_`;
        const parts = [];
        parts.push(getHtmlForParam({
            id: "name",
            name: "Name",
            description: "Name used in the dialogue script.",
            type: "text",
            default: voice.name || "",
            toggleable: false,
            view_type: "normal",
        }, prefix));
        if (voice.type === "custom") {
            parts.push(getHtmlForParam({
                id: "speaker",
                name: "Speaker",
                description: "Built-in CustomVoice speaker.",
                type: "dropdown",
                values: this.speakerOptions,
                default: voice.speaker || "Serena",
                toggleable: false,
                view_type: "normal",
            }, prefix));
        }
        parts.push(getHtmlForParam({
            id: "referenceText",
            name: "Reference Audio Text",
            description: "The reference text that describes the voice sample.",
            type: "text",
            default: voice.referenceText || "",
            toggleable: false,
            view_type: "big",
        }, prefix));
        if (voice.type === "custom" || voice.type === "design") {
            parts.push(getHtmlForParam({
                id: "styleInstruction",
                name: "Style Instruction",
                description: "Required instruction to guide the voice style.",
                type: "text",
                default: voice.styleInstruction || "",
                toggleable: false,
                view_type: "big",
            }, prefix));
        }
        content.insertAdjacentHTML("beforeend", parts.map(p => p.html).join(""));
        for (const p of parts) {
            try {
                p.runnable();
            }
            catch { }
        }
        if (voice.type === "audio") {
            const audioRow = document.createElement("div");
            audioRow.className = "auto-input";
            audioRow.innerHTML =
                `<label class="auto-input-label">Audio File</label>`
                    + `<input type="file" id="${prefix}audiofile" accept="audio/*" class="auto-input-field" />`
                    + `<div class="auto-input-desc">${this.escapeHtml(voice.audioFileName || "No file selected")}</div>`;
            content.appendChild(audioRow);
        }
        return wrap;
    }
    buildAddButtons() {
        const wrap = document.createElement("div");
        wrap.className = "qwentts-add-buttons";
        const atMax = this.voices.length >= 8;
        const addCustom = document.createElement("button");
        addCustom.className = "basic-button";
        addCustom.innerText = "+ Add Custom Voice";
        addCustom.disabled = atMax;
        addCustom.addEventListener("click", (e) => {
            e.preventDefault();
            this.addVoice("custom");
        });
        const addDesign = document.createElement("button");
        addDesign.className = "basic-button";
        addDesign.innerText = "+ Add Voice Design";
        addDesign.disabled = atMax;
        addDesign.addEventListener("click", (e) => {
            e.preventDefault();
            this.addVoice("design");
        });
        const addAudio = document.createElement("button");
        addAudio.className = "basic-button";
        addAudio.innerText = "+ Add Audio File";
        addAudio.disabled = atMax;
        addAudio.addEventListener("click", (e) => {
            e.preventDefault();
            this.addVoice("audio");
        });
        wrap.appendChild(addCustom);
        wrap.appendChild(addDesign);
        wrap.appendChild(addAudio);
        return wrap;
    }
    addVoice(type) {
        this.serializeVoicesFromUi();
        const voice = this.normalizeVoice({
            id: this.nextVoiceId(),
            type,
            name: type === "custom" ? "Serena" : type === "design" ? "Voice Design" : "Audio Voice",
            speaker: "Serena",
        });
        this.voices.push(voice);
        this.render();
        this.saveVoices();
    }
    removeVoice(voiceId) {
        this.serializeVoicesFromUi();
        this.voices = this.voices.filter(v => v.id !== voiceId);
        this.render();
        this.saveVoices();
    }
    installChangeListener() {
        if (!this.voiceList) {
            return;
        }
        if (this.voiceList.dataset.qwenttsListeners === "true") {
            return;
        }
        this.voiceList.dataset.qwenttsListeners = "true";
        this.voiceList.addEventListener("click", (e) => {
            const btn = e.target.closest('button[data-qwentts-action="remove-voice"]');
            if (!btn) {
                return;
            }
            e.preventDefault();
            e.stopPropagation();
            const voiceWrap = btn.closest("[data-qwentts-voice-id]");
            if (voiceWrap?.dataset.qwenttsVoiceId) {
                this.removeVoice(voiceWrap.dataset.qwenttsVoiceId);
            }
        });
        const handler = (e) => {
            const target = e.target;
            if (!target) {
                return;
            }
            const wrap = target.closest("[data-qwentts-voice-id]");
            if (!wrap || !wrap.dataset.qwenttsVoiceId) {
                return;
            }
            if (target instanceof HTMLInputElement && target.type === "file") {
                this.handleFileInput(target, wrap.dataset.qwenttsVoiceId);
                return;
            }
            const id = target.id;
            if (!id || !id.startsWith("qwentts_voice_")) {
                return;
            }
            this.scheduleVoiceSyncFromUi();
        };
        this.voiceList.addEventListener("input", handler, true);
        this.voiceList.addEventListener("change", handler, true);
    }
    handleFileInput(input, voiceId) {
        if (!input.files || input.files.length === 0) {
            return;
        }
        const file = input.files[0];
        const voice = this.voices.find(v => v.id === voiceId);
        if (!voice) {
            return;
        }
        const reader = new FileReader();
        reader.onload = () => {
            const result = `${reader.result || ""}`;
            const base64 = result.includes(",") ? result.split(",")[1] : result;
            voice.audioBase64 = base64;
            voice.audioFileName = file.name;
            this.saveVoices();
            this.render();
        };
        reader.onerror = () => showError("Failed to read audio file.");
        reader.readAsDataURL(file);
    }
    saveVoices() {
        if (!this.inputElem) {
            return;
        }
        const payload = this.voices.map(v => ({
            type: v.type,
            name: v.name,
            referenceText: v.referenceText,
            styleInstruction: v.styleInstruction,
            speaker: v.speaker,
            audioBase64: v.audioBase64,
        }));
        this.inputElem.value = JSON.stringify(payload);
        triggerChangeFor(this.inputElem);
    }
    /** Reads one voice's fields from the DOM and updates the in-memory voice. */
    updateVoiceFromUi(voiceId, voice) {
        const prefix = `qwentts_voice_${voiceId}_`;
        const getVal = (id) => {
            const el = document.getElementById(prefix + id);
            return (el?.value ?? "").trim();
        };
        voice.name = getVal("name");
        voice.referenceText = getVal("referenceText");
        voice.styleInstruction = getVal("styleInstruction");
        if (voice.type === "custom") {
            voice.speaker = getVal("speaker") || voice.speaker;
        }
    }
    /** Syncs all voices from DOM into this.voices and writes JSON to the hidden input. Call before Generate or a few ms after input. */
    serializeVoicesFromUi() {
        for (const voice of this.voices) {
            this.updateVoiceFromUi(voice.id, voice);
        }
        this.saveVoices();
    }
    scheduleVoiceSyncFromUi() {
        if (this.voiceSyncTimer) {
            clearTimeout(this.voiceSyncTimer);
        }
        this.voiceSyncTimer = setTimeout(() => {
            try {
                this.serializeVoicesFromUi();
            }
            catch { }
            this.voiceSyncTimer = null;
        }, 125);
    }
    /** True when the Qwen-TTS group toggle (#input_group_content_qwentts_toggle) is ON. */
    isGroupEnabled() {
        const toggler = document.getElementById("input_group_content_qwentts_toggle");
        return !toggler || !!toggler.checked;
    }
    /** Validation runs only when the Qwen-TTS toggle is ON; when off, returns null (no error). */
    validateVoices() {
        if (!this.isGroupEnabled()) {
            return null;
        }
        return null;
        if (this.voices.length === 0) {
            return "Qwen-TTS: add at least one voice.";
        }
        if (this.voices.length > 8) {
            return "Qwen-TTS: RoleBank supports up to 8 voices.";
        }
        for (const voice of this.voices) {
            if (!voice.name.trim()) {
                return "Qwen-TTS: each voice requires a name.";
            }
            if (voice.type === "custom" || voice.type === "design") {
                if (!voice.referenceText.trim()) {
                    return "Qwen-TTS: reference audio text is required for Custom Voice and Voice Design.";
                }
                if (!voice.styleInstruction.trim()) {
                    return "Qwen-TTS: style instruction is required for Custom Voice and Voice Design.";
                }
            }
            if (voice.type === "audio" && !voice.audioBase64) {
                return "Qwen-TTS: audio file is required for Audio File voices.";
            }
        }
        return null;
    }
    tryWrapGenerate() {
        if (!mainGenHandler || typeof mainGenHandler.doGenerate !== "function") {
            return false;
        }
        if (mainGenHandler.doGenerate.__qwenttsWrapped) {
            return true;
        }
        const original = mainGenHandler.doGenerate.bind(mainGenHandler);
        mainGenHandler.doGenerate = (...args) => {
            // When toggle is OFF, extension is DISABLED: do not modify anything, just pass through
            if (!this.isGroupEnabled()) {
                return original(...args);
            }
            this.serializeVoicesFromUi();
            const err = this.validateVoices();
            if (err) {
                showError(err);
                return;
            }
            return original(...args);
        };
        mainGenHandler.doGenerate.__qwenttsWrapped = true;
        return true;
    }
    startGenerateWrapRetry(intervalMs = 250) {
        if (this.genWrapInterval) {
            return;
        }
        const tryWrap = () => {
            try {
                if (this.tryWrapGenerate()) {
                    clearInterval(this.genWrapInterval);
                    this.genWrapInterval = null;
                }
            }
            catch { }
        };
        tryWrap();
        this.genWrapInterval = setInterval(tryWrap, intervalMs);
    }
    escapeHtml(text) {
        return (text || "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#39;");
    }
}
new QwenTTSUI();
addInstallButton('qwentts', 'qwen_tts', 'qwen_tts', 'Install Qwen-TTS Nodes');
//# sourceMappingURL=qwentts.js.map