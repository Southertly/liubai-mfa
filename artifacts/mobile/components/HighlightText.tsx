import React from "react";
import { StyleProp, Text, TextStyle } from "react-native";

import { useColors } from "@/hooks/useColors";

interface Props {
  text: string;
  query: string;
  style?: StyleProp<TextStyle>;
  highlightColor?: string;
  numberOfLines?: number;
}

/**
 * Renders `text` with all occurrences of `query` wrapped in a highlight span.
 * Case-insensitive. Falls back to a plain <Text> when query is empty.
 */
export function HighlightText({ text, query, style, highlightColor, numberOfLines }: Props) {
  const colors = useColors();
  const accentColor = highlightColor ?? colors.primary;

  if (!query.trim()) {
    return (
      <Text style={style} numberOfLines={numberOfLines}>
        {text}
      </Text>
    );
  }

  // Split while keeping the matched segments
  const regex = new RegExp(`(${escapeRegex(query.trim())})`, "gi");
  const parts = text.split(regex);

  return (
    <Text style={style} numberOfLines={numberOfLines}>
      {parts.map((part, i) =>
        regex.test(part) ? (
          <Text
            key={i}
            style={{
              color: accentColor,
              fontFamily: "Inter_700Bold",
              backgroundColor: accentColor + "20",
              borderRadius: 3,
            }}
          >
            {part}
          </Text>
        ) : (
          <Text key={i}>{part}</Text>
        )
      )}
    </Text>
  );
}

function escapeRegex(str: string): string {
  return str.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}
