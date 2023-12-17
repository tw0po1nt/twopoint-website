import { FC, PropsWithChildren } from "react";
import { FontAwesomeIcon } from "@fortawesome/react-fontawesome";
import { faChevronLeft } from "@fortawesome/free-solid-svg-icons";

export interface BackButtonProps {
  onClick: () => void;
}

const BackButton: FC<PropsWithChildren<BackButtonProps>> = ({ children, onClick }) => {
  return (
    <div className="flex items-center gap-2" onClick={onClick}>
      <FontAwesomeIcon size="1x" icon={faChevronLeft} />
      {children}
    </div>
  );
};

export default BackButton;
