import { useSyncExternalStore } from "react";

import { getSnapshot, subscribe, type TapeSnapshot } from "../bus/tape-store.ts";

export const useTape = (): TapeSnapshot => useSyncExternalStore(subscribe, getSnapshot);
