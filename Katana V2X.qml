// Katana V2X.qml — SignalRGB service panel UI

import QtQuick 2.15
import QtQuick.Controls 2.15
import QtQuick.Layouts 1.15

Item {
    id: root
    width: parent ? parent.width : 400
    height: content.implicitHeight + 32

    ColumnLayout {
        id: content
        anchors {
            top: parent.top
            left: parent.left
            right: parent.right
            margins: 16
        }
        spacing: 10

        Text {
            text: "Creative Sound Blaster Katana V2X"
            font.pixelSize: 14
            font.bold: true
            color: "#ffffff"
            Layout.fillWidth: true
        }

        Rectangle {
            Layout.fillWidth: true
            height: 1
            color: "#333333"
        }

        // Service requirement notice
        Rectangle {
            Layout.fillWidth: true
            height: noticeRow.implicitHeight + 16
            color: "#1a1a2e"
            radius: 5
            border.color: "#3a3a5c"
            border.width: 1

            RowLayout {
                id: noticeRow
                anchors {
                    left: parent.left
                    right: parent.right
                    verticalCenter: parent.verticalCenter
                    margins: 10
                }
                spacing: 8

                Rectangle {
                    width: 8; height: 8; radius: 4
                    color: "#f0a500"
                    SequentialAnimation on opacity {
                        loops: Animation.Infinite
                        NumberAnimation { to: 0.2; duration: 800; easing.type: Easing.InOutSine }
                        NumberAnimation { to: 1.0; duration: 800; easing.type: Easing.InOutSine }
                    }
                }

                Text {
                    text: "Requires V2XBridge service to be running"
                    font.pixelSize: 11
                    color: "#cccccc"
                    wrapMode: Text.WordWrap
                    Layout.fillWidth: true
                }
            }
        }

        // Setup steps
        Rectangle {
            Layout.fillWidth: true
            height: stepsText.implicitHeight + 20
            color: "#111111"
            radius: 5
            border.color: "#2a2a2a"
            border.width: 1

            Text {
                id: stepsText
                anchors {
                    left: parent.left
                    right: parent.right
                    top: parent.top
                    margins: 10
                }
                text: "First-time setup (run once):\n\n" +
                      "1.  Open the addon folder (button below)\n" +
                      "2.  Right-click V2XBridge.exe → Run as administrator\n" +
                      "       or from PowerShell:\n" +
                      "       .\\V2XBridge.exe --install\n\n" +
                      "The bridge installs itself as a Windows service\n" +
                      "and starts automatically on every boot."
                font.pixelSize: 11
                font.family: "Consolas"
                color: "#888899"
                wrapMode: Text.WordWrap
                lineHeight: 1.4
            }
        }

        // Open addon folder button
        Rectangle {
            Layout.fillWidth: true
            height: 32
            color: openArea.containsMouse ? "#2a2a4a" : "#1e1e3a"
            radius: 5
            border.color: openArea.containsMouse ? "#5566aa" : "#3a3a5c"
            border.width: 1

            Text {
                anchors.centerIn: parent
                text: "Open addon folder"
                font.pixelSize: 11
                color: "#8899cc"
            }

            MouseArea {
                id: openArea
                anchors.fill: parent
                hoverEnabled: true
                cursorShape: Qt.PointingHandCursor
                onClicked: Qt.openUrlExternally(Qt.resolvedUrl("."))
            }
        }

        // Links
        RowLayout {
            Layout.fillWidth: true
            spacing: 0

            Text {
                text: "Source: "
                font.pixelSize: 10
                color: "#666666"
            }
            Text {
                text: "github.com/capkz/V2X-SignalRGB"
                font.pixelSize: 10
                color: "#5588cc"
            }
            Item { Layout.fillWidth: true }
            Text {
                text: "by capkz"
                font.pixelSize: 10
                color: "#555555"
            }
        }

        Item { height: 2 }
    }
}
