import UIKit

@MainActor enum DotNativeWheelTimePickerPlugin {
  private static var active: NativeTimeDialog?
  static func register() {
    NativeChannels.channel("dotnative.wheeltimepicker").onReset = { active?.cancel() }
    NativeChannels.channel("dotnative.wheeltimepicker").handle("show") { args, reply in
      guard let options = TimeOptions(args) else {
        reply.failure("invalid_arguments", "Expected valid time picker options")
        return
      }
      guard active == nil, let presenter = NativeChannels.presenter,
        presenter.presentedViewController == nil
      else {
        reply.failure("picker_unavailable", "Another dialog is already presented")
        return
      }
      let dialog = NativeTimeDialog(options: options) { time in
        active = nil
        reply.success(time.map { .string($0) } ?? .null)
      }
      reply.onCancel = { [weak dialog] in dialog?.cancel() }
      active = dialog
      presenter.present(dialog, animated: true)
    }
    NativeChannels.channel("dotnative.wheeltimepicker").handle("cancel") { _, reply in
      active?.cancel()
      reply.success(.null)
    }
  }
}

@MainActor private final class NativeTimeDialog: UIViewController {
  private let picker = UIDatePicker()
  private let panel = UIView()
  private let completion: (String?) -> Void
  private let titleText: String
  private let saveText: String
  private let cancelText: String
  private let initial: Date
  private let interval: Int
  private let is24Hour: Bool
  private let locale: Locale?
  private var finished = false
  private let background: UIColor
  private let foreground: UIColor
  private let font: UIFont

  init(options: TimeOptions, completion: @escaping (String?) -> Void) {
    titleText = options.title
    saveText = options.confirm
    cancelText = options.cancel
    initial = options.initial
    interval = options.interval
    is24Hour = options.is24Hour
    locale = options.locale
    background = options.background
    foreground = options.foreground
    font = options.font
    self.completion = completion
    super.init(nibName: nil, bundle: nil)
    modalPresentationStyle = .overFullScreen
    modalTransitionStyle = .crossDissolve
  }
  required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }

  override func viewDidLoad() {
    super.viewDidLoad()
    view.backgroundColor = UIColor.black.withAlphaComponent(0.4)
    let backdrop = UIButton()
    backdrop.addTarget(self, action: #selector(cancel), for: .touchUpInside)
    backdrop.frame = view.bounds
    backdrop.autoresizingMask = [.flexibleWidth, .flexibleHeight]
    view.addSubview(backdrop)
    panel.backgroundColor = background
    panel.layer.cornerRadius = 28
    panel.clipsToBounds = true
    view.addSubview(panel)
    let title = UILabel()
    title.tag = 1
    title.text = titleText
    title.font = .boldSystemFont(ofSize: 24)
    title.numberOfLines = 2
    title.textColor = foreground
    panel.addSubview(title)
    picker.datePickerMode = .time
    picker.preferredDatePickerStyle = .wheels
    picker.minuteInterval = interval
    picker.locale = locale ?? Locale(identifier: is24Hour ? "en_GB" : "en_US")
    picker.date = initial
    panel.addSubview(picker)
    let save = UIButton(type: .system)
    save.tag = 2
    save.setTitle(saveText, for: .normal)
    save.titleLabel?.font = font
    save.setTitleColor(foreground, for: .normal)
    save.addTarget(self, action: #selector(confirm), for: .touchUpInside)
    panel.addSubview(save)
    let cancel = UIButton(type: .system)
    cancel.tag = 3
    cancel.setTitle(cancelText, for: .normal)
    cancel.titleLabel?.font = font
    cancel.setTitleColor(foreground, for: .normal)
    cancel.addTarget(self, action: #selector(self.cancel), for: .touchUpInside)
    panel.addSubview(cancel)
  }

  override func viewDidLayoutSubviews() {
    super.viewDidLayoutSubviews()
    let width = min(328, view.bounds.width - 80)
    panel.frame = CGRect(
      x: (view.bounds.width - width) / 2, y: (view.bounds.height - 276) / 2, width: width,
      height: 312)
    panel.viewWithTag(1)?.frame = CGRect(x: 24, y: 20, width: width - 48, height: 64)
    picker.frame = CGRect(x: 14, y: 92, width: width - 28, height: 160)
    panel.viewWithTag(2)?.frame = CGRect(x: width - 184, y: 260, width: 88, height: 44)
    panel.viewWithTag(3)?.frame = CGRect(x: width - 96, y: 260, width: 88, height: 44)
  }

  @objc private func confirm() {
    let formatter = DateFormatter()
    formatter.calendar = Calendar(identifier: .gregorian)
    formatter.locale = Locale(identifier: "en_US_POSIX")
    formatter.dateFormat = "HH:mm"
    finish(formatter.string(from: picker.date))
  }
  @objc func cancel() { finish(nil) }
  private func finish(_ time: String?) {
    guard !finished else { return }
    finished = true
    dismiss(animated: true) { self.completion(time) }
  }
}

@MainActor private struct TimeOptions {
  let initial: Date
  let interval: Int
  let is24Hour: Bool
  let locale: Locale?
  let title: String
  let confirm: String
  let cancel: String
  let background: UIColor
  let foreground: UIColor
  let font: UIFont

  init?(_ args: PluginValue) {
    guard case .map(let map) = args else { return nil }
    func text(_ key: String) -> String? {
      if case .string(let value)? = map[key] { return value }
      return nil
    }
    let formatter = DateFormatter()
    formatter.calendar = Calendar(identifier: .gregorian)
    formatter.locale = Locale(identifier: "en_US_POSIX")
    formatter.dateFormat = "HH:mm"
    formatter.isLenient = false
    guard let initialText = text("initialTime"), let date = formatter.date(from: initialText),
      let title = text("title"), let confirm = text("confirmText"), let cancel = text("cancelText")
    else { return nil }
    let interval: Int
    if case .integer(let value)? = map["minuteInterval"] { interval = Int(value) } else { return nil }
    guard (1...30).contains(interval), 60 % interval == 0 else { return nil }
    let calendar = Calendar(identifier: .gregorian)
    let minute = calendar.component(.minute, from: date)
    guard minute % interval == 0 else { return nil }
    self.interval = interval
    initial = date
    if case .bool(let value)? = map["is24Hour"] { is24Hour = value } else { is24Hour = true }
    locale = text("locale").map { Locale(identifier: $0) }
    self.title = title
    self.confirm = confirm
    self.cancel = cancel
    func color(_ key: String, _ fallback: Int64) -> UIColor {
      let bits: Int64
      if case .integer(let value)? = map[key] { bits = value } else { bits = fallback }
      return UIColor(
        red: CGFloat((bits >> 16) & 255) / 255, green: CGFloat((bits >> 8) & 255) / 255,
        blue: CGFloat(bits & 255) / 255, alpha: CGFloat((bits >> 24) & 255) / 255)
    }
    background = color("backgroundColor", 0xfff6_f6f6)
    foreground = color("textColor", 0xff00_0000)
    let size: CGFloat
    if case .double(let value)? = map["fontSize"], value.isFinite, value > 0 {
      size = CGFloat(value)
    } else {
      size = 16
    }
    font = text("fontFamily").flatMap { UIFont(name: $0, size: size) } ?? .systemFont(ofSize: size)
  }
}
