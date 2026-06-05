// react-dom ships without bundled types and this project doesn't depend on
// @types/react-dom (it's only pulled in by a focus-trap DOM test). Declare the
// sliver of the client API that test uses so the strict build stays clean
// without adding a devDependency.
declare module 'react-dom/client' {
  export type Root = { render(node: unknown): void; unmount(): void };
  export function createRoot(container: Element): Root;
}
