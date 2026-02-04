declare const mainGenHandler: {
    doGenerate: ((...args: unknown[]) => void) & { __qwenttsWrapped?: boolean };
};

declare function showError(message: string): void;
declare function triggerChangeFor(element: HTMLElement): void;
declare function doToggleEnable(prefix: string): void;
declare function findParentOfClass(element: Element, className: string): HTMLElement;
declare function getHtmlForParam(param: Record<string, any>, prefix: string): { html: string; runnable: () => void };

declare const promptTabComplete: {
    registerPrefix(prefix: string, description: string, hintFn: () => string[], ...args: unknown[]): void;
};

declare let postParamBuildSteps: (() => void)[] | undefined;
declare const currentBackendFeatureSet: string[] | undefined;

declare function addInstallButton(
    groupId: string,
    featureId: string,
    installId: string,
    buttonText: string
): void;
