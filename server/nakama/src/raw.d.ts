/** `file?raw` imports a file's exact text (scripts/build.mjs, and Vite in the tests). */
declare module "*?raw" {
  const text: string;
  // eslint-disable-next-line import-x/no-default-export -- the shape `?raw` imports have
  export default text;
}
