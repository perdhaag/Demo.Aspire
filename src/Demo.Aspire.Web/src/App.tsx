import { BusTape } from "./components/BusTape.tsx";
import { ChaosStrip } from "./components/ChaosStrip.tsx";
import { FlowSection } from "./components/FlowSection.tsx";
import { Footer } from "./components/Footer.tsx";
import { Ledgers } from "./components/Ledgers.tsx";
import { Masthead } from "./components/Masthead.tsx";
import { ScreeningList } from "./components/ScreeningList.tsx";
import { SeatingSection } from "./components/SeatingSection.tsx";
import { ToastProvider } from "./components/Toast.tsx";
import { DemoDataProvider } from "./data/demo-data.tsx";
import { SelectionProvider } from "./data/selection.tsx";

export function App() {
    return (
        <ToastProvider>
            <SelectionProvider>
                <DemoDataProvider>
                    <Masthead />

                    <div className="layout">
                        <main>
                            <ScreeningList />
                            <SeatingSection />
                            <ChaosStrip />
                            <FlowSection />
                            <Ledgers />
                        </main>

                        <BusTape />
                    </div>

                    <Footer />
                </DemoDataProvider>
            </SelectionProvider>
        </ToastProvider>
    );
}
