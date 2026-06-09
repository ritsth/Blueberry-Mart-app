import React, { useRef } from 'react';
import { PlatformPressable } from '@react-navigation/elements';
import { useTour } from '../context/TourContext';

/**
 * A drop-in `tabBarButton` that renders the default tab button (children include the
 * icon/label/badge) while measuring its on-screen rect and reporting it to the tour
 * registry under `tourKey`.
 */
function MeasuredTabButton({ tourKey, ...props }: any) {
  const ref = useRef<any>(null);
  const tour = useTour();
  return (
    <PlatformPressable
      {...props}
      ref={ref}
      onLayout={() => {
        ref.current?.measureInWindow?.((x: number, y: number, w: number, h: number) =>
          tour?.register(tourKey, { x, y, w, h }));
      }}
    />
  );
}

/** Factory for use as `options={{ tabBarButton: tabButton('Shop') }}`. */
export const tabButton = (tourKey: string) => {
  const TabButton = (props: any) => <MeasuredTabButton tourKey={tourKey} {...props} />;
  TabButton.displayName = `TabButton(${tourKey})`;
  return TabButton;
};
